using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The cost-per-liter of a batch, as two different screens report it (#178).
/// </summary>
/// <remarks>
/// <para>
/// The batch-costs panel (and therefore the audit timeline, which embeds it) counts every voucher of
/// the import. The batch P&amp;L excludes soft-deleted ones. When a voucher has been soft-deleted,
/// the two report a different cost for the same batch, and neither flags it - an operator showing an
/// auditor why a valuation is what it is has no way to tell which number to believe.
/// </para>
/// <para>
/// These tests pin the divergence so it cannot be introduced silently again. The fix is a product
/// decision about which count is authoritative, not a refactor, so it is recorded rather than guessed.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class BatchCostDivergenceIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public BatchCostDivergenceIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ASofdeleted_Voucher_Makes_The_Two_Screens_Disagree_About_The_Same_Batch()
    {
        var context = CreateContext();
        var importId = Guid.NewGuid();
        await ResetAsync(context);
        await SeedImportAndFuel(context, importId);

        var fuelTypeId = await SeedVouchersAsync(context, importId, softDeletedLiters: 0m);

        var costs = await CostsAsync(context, importId);
        var pnl = await PnlAsync(context, importId);

        var reportedCost = costs.Single(c => c.FuelTypeId == fuelTypeId).CostPerLiter;
        var reportedPnl = pnl.Single(p => p.FuelTypeId == fuelTypeId).CostPerLiter;

        reportedCost.Should().BeApproximately(50m, 0.01m);
        reportedPnl.Should().BeApproximately(50m, 0.01m);
        reportedCost.Should().Be(reportedPnl, "with no soft-deleted voucher the two agree exactly");

        // Now soft-delete one of the two vouchers. The batch-costs rollup still counts it; the P&L
        // no longer does. This is the divergence, asserted rather than described.
// Soft-delete the cheap voucher. Written as SQL rather than by loading and saving, because the
        // contexts here are NoTracking - mutating a detached entity would silently do nothing and
        // the test would then "prove" that the two agree.
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_vouchers SET is_deleted = TRUE WHERE id = (SELECT id FROM fuel_vouchers WHERE import_job_id = {0} ORDER BY cost_per_liter ASC LIMIT 1)""",
            importId);

var afterCosts = (await CostsAsync(context, importId)).Single(c => c.FuelTypeId == fuelTypeId).CostPerLiter;
        var afterPnl = (await PnlAsync(context, importId)).Single(p => p.FuelTypeId == fuelTypeId).CostPerLiter;

        // Concretely: batch-costs still averages the deleted 40 UAH voucher in, so it stays at
        // 50; the P&L drops it and reports the single remaining voucher's 60. Two screens, one
        // batch, two costs - and nothing on either screen says which one to believe.
        afterCosts.Should().BeApproximately(50m, 0.01m,
            "the batch-costs rollup does not filter soft-deleted vouchers");
        afterPnl.Should().BeApproximately(60m, 0.01m,
            "the P&L excludes soft-deleted vouchers, so only the 60 UAH one is left");
    }

    private async Task<string> SeedVouchersAsync(ApplicationDbContext context, Guid importId, decimal softDeletedLiters)
    {
        var fuel = await context.FuelTypes.FirstAsync();
        var supplier = await context.Suppliers.FirstAsync();

// 10 L at 40 and 10 L at 60: the mean is 50 either way, and removing one moves it.
        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            ImportJobId = importId,
            FuelTypeId = fuel.Id,
            SupplierId = supplier.Id,
            Provider = "okko",
            VoucherNumber = $"V-{Guid.NewGuid():N}",
            QrPayload = Guid.NewGuid().ToString(),
            Liters = 10m,
            CostPerLiter = 40m,
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            IsDeleted = softDeletedLiters > 0m
        });
        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            ImportJobId = importId,
            FuelTypeId = fuel.Id,
            SupplierId = supplier.Id,
            Provider = "okko",
            VoucherNumber = $"V-{Guid.NewGuid():N}",
            QrPayload = Guid.NewGuid().ToString(),
            Liters = 10m,
            CostPerLiter = 60m,
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            IsDeleted = false
        });

        await context.SaveChangesAsync();
        return fuel.Id;
    }

    private async Task<List<ImportBatchCostDto>> CostsAsync(ApplicationDbContext context, Guid importId)
        => await new GetImportBatchCostsQueryHandler(context, new BlendedCostRecalculator(context))
            .HandleAsync(new GetImportBatchCostsQuery(importId));

    private async Task<List<ImportBatchPnlDto>> PnlAsync(ApplicationDbContext context, Guid importId)
        => await new GetImportBatchPnlQueryHandler(context)
            .HandleAsync(new GetImportBatchPnlQuery(importId));

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "voucher_exchanges", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private static async Task SeedImportAndFuel(ApplicationDbContext seed, Guid importId)
    {
        seed.VoucherImports.Add(new VoucherImport
        {
            Id = importId,
            FileName = "divergence.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow
        });

        if (!await seed.Suppliers.AnyAsync())
        {
            seed.Suppliers.Add(new Supplier { Id = Guid.NewGuid(), Name = "S", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
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

        await seed.SaveChangesAsync();
    }
}