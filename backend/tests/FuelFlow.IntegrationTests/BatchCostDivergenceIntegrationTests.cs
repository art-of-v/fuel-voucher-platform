using FuelFlow.Features.Pricing;
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
/// The batch-costs panel (and the audit timeline, which embeds it) used to count soft-deleted
/// vouchers while the batch P&amp;L did not, so one batch reported two different costs depending on
/// which screen an operator opened - an operator showing an auditor why a valuation is what it is
/// had no way to tell which number to believe.
/// </para>
/// <para>
/// The rule now is that a deleted voucher does not exist for costing purposes. This test holds both
/// screens to it.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class BatchCostDivergenceIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public BatchCostDivergenceIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

[Fact]
    public async Task A_Softdeleted_Voucher_Stops_Counting_Towards_The_Cost_On_Both_Screens()
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

        // Concretely: the 40 UAH voucher is gone, so both screens report the single remaining
        // voucher's 60. Before #980 this panel said 50 while the P&L said 60 - one batch, two costs.
        afterCosts.Should().BeApproximately(60m, 0.01m,
            "a soft-deleted voucher must not keep influencing the batch cost");
        afterPnl.Should().BeApproximately(60m, 0.01m);
        afterCosts.Should().Be(afterPnl,
            "the two screens must never disagree about the cost of the same batch");
    }

    // The stock figures follow the cost: counting a deleted voucher in the count while excluding it
    // from the cost would leave the panel describing stock that does not exist.
    [Fact]
    public async Task A_Softdeleted_Voucher_Leaves_The_Count_And_Litres_Together()
    {
        var context = CreateContext();
        var importId = Guid.NewGuid();
        await ResetAsync(context);
        await SeedImportAndFuel(context, importId);
        var fuelTypeId = await SeedVouchersAsync(context, importId, softDeletedLiters: 0m);

        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_vouchers SET is_deleted = TRUE WHERE id = (SELECT id FROM fuel_vouchers WHERE import_job_id = {0} ORDER BY cost_per_liter ASC LIMIT 1)""",
            importId);

        var costs = (await CostsAsync(context, importId)).Single(c => c.FuelTypeId == fuelTypeId);
        var pnl = (await PnlAsync(context, importId)).Single(p => p.FuelTypeId == fuelTypeId);

        costs.VoucherCount.Should().Be(1, "a deleted voucher is not stock");
        costs.TotalLiters.Should().Be(10m);
        costs.VoucherCount.Should().Be(pnl.VouchersIn, "both screens must count the same stock");
    }

    // The two screens above were fixed by #980/#981. The blended POOL was not: it is the number that
    // reprices packages and therefore what a customer is quoted, and it ran its own query with
    // IgnoreQueryFilters, which lifts the model's global !IsDeleted filter (#216).
    [Fact]
    public async Task The_Blended_Pool_Excludes_Softdeleted_Vouchers()
    {
        var context = CreateContext();
        var importId = Guid.NewGuid();
        await ResetAsync(context);
        await SeedImportAndFuel(context, importId);

        var fuelTypeId = await SeedVouchersAsync(context, importId, softDeletedLiters: 0m);
        var recalculator = new BlendedCostRecalculator(context, new OfferPriceRecorder(context));

        (await recalculator.ComputeBlendedAsync(fuelTypeId)).Should().BeApproximately(50m, 0.01m,
            "10 L at 40 and 10 L at 60 blend to 50");

        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_vouchers SET is_deleted = TRUE WHERE id = (SELECT id FROM fuel_vouchers WHERE import_job_id = {0} ORDER BY cost_per_liter ASC LIMIT 1)""",
            importId);

        var afterDelete = await recalculator.ComputeBlendedAsync(fuelTypeId);

        afterDelete.Should().BeApproximately(60m, 0.01m,
            "the deleted 40 UAH voucher must leave the pool, so the blend is the surviving voucher alone");
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
            Brand = "okko",
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
            Brand = "okko",
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
        => await new GetImportBatchCostsQueryHandler(context, new BlendedCostRecalculator(context, new OfferPriceRecorder(context)))
            .HandleAsync(new GetImportBatchCostsQuery(importId));

    private async Task<List<ImportBatchPnlDto>> PnlAsync(ApplicationDbContext context, Guid importId)
        => await new GetImportBatchPnlQueryHandler(context)
            .HandleAsync(new GetImportBatchPnlQuery(importId));

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static async Task ResetAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "voucher_exchanges", "movement_batches", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private static async Task SeedImportAndFuel(ApplicationDbContext seed, Guid importId)
    {
        seed.VoucherImports.Add(new VoucherImport
        {
            Id = importId,
            FileName = "divergence.pdf",
            CompletedAtUtc = DateTime.UtcNow,
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
