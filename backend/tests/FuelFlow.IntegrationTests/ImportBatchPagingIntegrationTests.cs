using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetImportBatches;
using FuelFlow.Persistence;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The import batch's vouchers, a page at a time, and the two buttons that act on all of them.
/// </summary>
/// <remarks>
/// <para>
/// The list used to be the whole batch: a supplier PDF is a thousand vouchers, and the tab
/// answered with a thousand rows to draw a table that shows a screenful.
/// </para>
/// <para>
/// The two buttons used to act on the ids the tab had loaded, which was the whole batch only
/// because the list was unbounded. With a page, those ids are a page - and a button that says
/// "activate all" and activates a page is a button that lies. So the action is the server's: it
/// names the import, and the database decides which rows that is.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ImportBatchPagingIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ImportBatchPagingIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_list_is_a_Page_And_Carries_The_Size_Of_The_Batch()
    {
        var importId = await SeedBatchAsync(7);

        await using var context = CreateContext();
        var page = await new GetImportBatchVouchersQueryHandler(context)
            .HandleAsync(new GetImportBatchVouchersQuery(importId, Page: 1, PageSize: 3));

        page.Items.Should().HaveCount(3, "a page holds a page");
        page.TotalCount.Should().Be(7, "the admin has to know what the page is a part of");
        page.TotalPages.Should().Be(3);
        page.HasNextPage.Should().BeTrue();
        page.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task The_Last_Page_Holds_What_Is_Left()
    {
        var importId = await SeedBatchAsync(7);

        await using var context = CreateContext();
        var page = await new GetImportBatchVouchersQueryHandler(context)
            .HandleAsync(new GetImportBatchVouchersQuery(importId, Page: 3, PageSize: 3));

        page.Items.Should().HaveCount(1);
        page.HasNextPage.Should().BeFalse();
        page.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task A_Page_Out_Of_Range_Is_Empty_And_Says_How_Many_There_Are()
    {
        var importId = await SeedBatchAsync(2);

        await using var context = CreateContext();
        var page = await new GetImportBatchVouchersQueryHandler(context)
            .HandleAsync(new GetImportBatchVouchersQuery(importId, Page: 9, PageSize: 50));

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task The_Whole_Batch_Is_Activated_Whatever_Page_The_Tab_Holds()
    {
        var importId = await SeedBatchAsync(5, withCost: true);

        await using (var context = CreateContext())
        {
            var result = await new ImportBatchBulkActionQueryHandler(context)
                .HandleAsync(new ImportBatchBulkActionQuery(importId, "activate", ActorId()));

            result.Success.Should().BeTrue();
            result.Affected.Should().Be(5, "the action is the server's, so it acts on the whole batch");
        }

        await using var verify = CreateContext();
        var statuses = await verify.FuelVouchers
            .Where(v => v.ImportJobId == importId)
            .Select(v => v.Status)
            .ToListAsync();

        statuses.Should().OnlyContain(s => s == VoucherStatus.Available);
    }

    [Fact]
    public async Task A_Batch_That_Is_Not_Fully_Costed_Is_Refused_Whole()
    {
        // The gate the id-based action has: never put on sale stock that has no cost of its own,
        // because the pricing engine would have nothing to price from.
        var importId = await SeedBatchAsync(3, withCost: false);

        await using var context = CreateContext();
        var result = await new ImportBatchBulkActionQueryHandler(context)
            .HandleAsync(new ImportBatchBulkActionQuery(importId, "activate", ActorId()));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Deactivating_Leaves_The_Other_Statuses_Alone()
    {
        var importId = await SeedBatchAsync(3, withCost: true);

        await using (var context = CreateContext())
        {
            var result = await new ImportBatchBulkActionQueryHandler(context)
                .HandleAsync(new ImportBatchBulkActionQuery(importId, "deactivate", ActorId()));

            result.Success.Should().BeTrue();
            result.Affected.Should().Be(3);
        }

        await using var verify = CreateContext();
        var statuses = await verify.FuelVouchers
            .Where(v => v.ImportJobId == importId)
            .Select(v => v.Status)
            .ToListAsync();

        statuses.Should().OnlyContain(s => s == VoucherStatus.Deactivated);
    }

    [Fact]
    public async Task An_Unknown_Action_Is_Refused()
    {
        var importId = await SeedBatchAsync(1);

        await using var context = CreateContext();
        var result = await new ImportBatchBulkActionQueryHandler(context)
            .HandleAsync(new ImportBatchBulkActionQuery(importId, "explode", ActorId()));

        result.Success.Should().BeFalse();
    }

    private static Guid ActorId() => Guid.NewGuid();

    /// <summary>
    /// One import's batch of vouchers, all imported and not yet on sale.
    /// </summary>
    private async Task<Guid> SeedBatchAsync(int count, bool withCost = true)
    {
        var importId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var brandId = Guid.Empty;
        var fuelTypeId = Guid.NewGuid();

        await using var context = CreateContext();

        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{Guid.NewGuid():N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        // The seeded network, used as it is: the slug is unique, so a second brand with the same
        // slug is refused.
        var brand = await context.Brands.FirstAsync(b => b.Slug == "okko");
        brandId = brand.Id;

        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelTypeId,
            Name = "Batch fuel " + Guid.NewGuid().ToString("N")[..8],
            BrandId = brandId,
            BasePrice = 50m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        context.VoucherImports.Add(new VoucherImport
        {
            Id = importId,
            FileName = $"batch-{Guid.NewGuid():N}.pdf",
            PageCount = count,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            Status = "Completed",
            ImportedCount = count
        });

        for (var index = 0; index < count; index++)
        {
            context.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Brand = "okko",
                FuelTypeId = fuelTypeId,
                Liters = 50m,
                CostPerLiter = withCost ? 45m : null,
                VoucherNumber = $"BATCH-{Guid.NewGuid():N}"[..20],
                QrPayload = Guid.NewGuid().ToString("N"),
                Status = VoucherStatus.Imported,
                ImportJobId = importId,
                SupplierExpirationDate = new DateOnly(2027, 1, 1),
                CustomerExpirationDate = new DateOnly(2027, 1, 1),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return importId;
    }
}
