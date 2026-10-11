using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Bulk deletion of vouchers is soft, never hard (#217).
/// </summary>
/// <remarks>
/// <para>
/// Owner policy: soft delete everywhere, always. Only garbage that a cleanup job removes may be
/// hard-deleted, and a voucher is never garbage - it may carry a cost an operator typed, be the
/// surrendered half of an exchange, or be the stock a valuation was computed from.
/// </para>
/// <para>
/// The old implementation was hard, and accidentally guarded rather than safe: four RESTRICT
/// foreign keys made it throw for any voucher that had been fulfilled, exchanged or renewed, so it
/// destroyed precisely the vouchers that had no history and refused the rest with a raw FK error.
/// These tests pin the corrected behaviour, including that the FK failure mode is gone.
/// </para>
/// <para>
/// Real PostgreSQL throughout. An in-memory provider supports neither <c>ExecuteUpdate</c> nor the
/// foreign keys, so a unit test here could have proved nothing about either.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class BulkVoucherSoftDeleteIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public BulkVoucherSoftDeleteIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

[Fact]
    public async Task DeleteAll_Flags_The_Vouchers_Instead_Of_Removing_The_Rows()
    {
        await SeedAsync();
        var context = CreateContext();

        var result = await Handler(context).HandleAsync(new BulkActionVouchersCommand("delete_all", null, null));

        result.Success.Should().BeTrue();
        result.Count.Should().Be(2);

        // The rows are still there. That is the whole point: history outlives the operator's
        // decision to remove a voucher from the shelf.
        (await context.FuelVouchers.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await context.FuelVouchers.IgnoreQueryFilters().CountAsync(v => v.IsDeleted)).Should().Be(2);

        // And they are gone from every ordinary query, via the global filter.
        (await context.FuelVouchers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Delete_By_Selection_Flags_Only_Those_Selected()
    {
        await SeedAsync();
        var context = CreateContext();
        var all = await context.FuelVouchers.IgnoreQueryFilters().ToListAsync();
        var target = all[0];

        var result = await Handler(context).HandleAsync(
            new BulkActionVouchersCommand("delete", [target.Id], null));

        result.Success.Should().BeTrue();
        var deleted = await context.FuelVouchers.IgnoreQueryFilters()
            .Where(v => v.IsDeleted)
            .Select(v => v.Id)
            .ToListAsync();

        deleted.Should().ContainSingle().Which.Should().Be(target.Id);
    }

    // The old hard delete threw a raw foreign-key error here. Soft delete has no such constraint,
    // so an operator can remove a voucher that is part of history instead of being handed an
    // exception from deep inside the database driver.
    [Fact]
    public async Task A_Voucher_That_History_References_Can_Still_Be_Soft_Deleted()
    {
        await SeedAsync();
        var context = CreateContext();
        var voucher = await context.FuelVouchers.IgnoreQueryFilters().FirstAsync();

        var userId = Guid.NewGuid();
        context.Users.Add(new User { Id = userId, PhoneNumber = "+380000007777", CreatedAtUtc = DateTime.UtcNow });
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 100m,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        context.Fulfillments.Add(new Fulfillment
        {
            OrderId = (await context.Orders.FirstAsync()).Id,
            VoucherId = voucher.Id
        });
        await context.SaveChangesAsync();

        // Fresh context for the action, as a request would get from DI.
        var acting = CreateContext();
        var result = await Handler(acting).HandleAsync(
            new BulkActionVouchersCommand("delete", [voucher.Id], null));

        result.Success.Should().BeTrue("deleting a voucher must not fail because history points at it");

        // The fulfillment is still there - it is history, and deleting a voucher must not take it.
        (await context.Fulfillments.CountAsync()).Should().Be(1);
        (await acting.FuelVouchers.IgnoreQueryFilters()
            .Where(v => v.Id == voucher.Id && v.IsDeleted)
            .ToListAsync())
            .Should().ContainSingle();
    }

    private BulkActionVouchersCommandHandler Handler(ApplicationDbContext context)
        => new(
            context,
            new Mock<IBackgroundJobClient>().Object,
            new ProviderEventService(context));

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

private async Task SeedAsync()
    {
        var seed = CreateContext();
        await ResetAsync(seed);

        if (!await seed.Suppliers.AnyAsync())
        {
            seed.Suppliers.Add(new Supplier
            {
                Id = Guid.NewGuid(),
                Name = "S",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        if (!await seed.FuelTypes.AnyAsync())
        {
            seed.FuelTypes.Add(new FuelTypeEntity
            {
                Id = "okko-dp",
                Name = "Дизель",
                StationId = "okko",
                BasePrice = 60m,
                DiscountPrice = 60m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        seed.VoucherImports.Add(new VoucherImport
        {
            Id = Guid.NewGuid(),
            FileName = "soft.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();

        var importId = (await seed.VoucherImports.FirstAsync()).Id;
        var fuel = await seed.FuelTypes.FirstAsync();
        var supplier = await seed.Suppliers.FirstAsync();

        foreach (var cost in new[] { 40m, 60m })
        {
            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.NewGuid(),
                ImportJobId = importId,
                FuelTypeId = fuel.Id,
                SupplierId = supplier.Id,
                Provider = "okko",
                VoucherNumber = $"V-{Guid.NewGuid():N}",
                QrPayload = Guid.NewGuid().ToString(),
                Liters = 10m,
                CostPerLiter = cost,
                Status = VoucherStatus.Imported,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

await seed.SaveChangesAsync();
    }

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "voucher_exchanges", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
}