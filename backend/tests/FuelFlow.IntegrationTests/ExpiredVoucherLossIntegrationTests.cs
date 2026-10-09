using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the nightly ExpiredVoucherLossService (pricing slice 4) against a real PostgreSQL container:
/// when enabled it retires only operator-owned, still-sellable stock whose printed expiration date has
/// passed to <see cref="VoucherStatus.Expired"/>, leaving future-dated stock, already-sold (Assigned/Used)
/// vouchers and same-day expiries untouched; when disabled it is a pure dry-run that mutates nothing.
/// </summary>
[Collection("Integration Tests")]
public sealed class ExpiredVoucherLossIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private static readonly Guid ImportId = Guid.NewGuid();

    public ExpiredVoucherLossIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BookExpiredLoss_WhenEnabled_RetiresOnlyLapsedUnsoldInStock()
    {
        await SeedAsync(seed =>
        {
            Enable(seed);
            SeedFixture(seed);
        });

        await RunAsync();

        using var verify = CreateContext();
        // The three lapsed, still-sellable vouchers are the only ones retired.
        (await StatusOf(verify, "instock-imported")).Should().Be(VoucherStatus.Expired);
        (await StatusOf(verify, "instock-available")).Should().Be(VoucherStatus.Expired);
        (await StatusOf(verify, "instock-warnings")).Should().Be(VoucherStatus.Expired);

        // Everything else is left exactly as seeded.
        (await StatusOf(verify, "future-instock")).Should().Be(VoucherStatus.Imported);
        (await StatusOf(verify, "today-instock")).Should().Be(VoucherStatus.Imported);
        (await StatusOf(verify, "lapsed-assigned")).Should().Be(VoucherStatus.Assigned);
        (await StatusOf(verify, "lapsed-used")).Should().Be(VoucherStatus.Used);

        (await verify.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Expired)).Should().Be(3);
    }

    [Fact]
    public async Task BookExpiredLoss_WhenDisabled_MutatesNothing()
    {
        await SeedAsync(seed =>
        {
            // ExpiredVoucherLoss:Enabled intentionally NOT set -> fail-safe default of false (dry-run).
            SeedFixture(seed);
        });

        await RunAsync();

        using var verify = CreateContext();
        (await verify.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Expired)).Should().Be(0);
        (await StatusOf(verify, "instock-imported")).Should().Be(VoucherStatus.Imported);
        (await StatusOf(verify, "instock-available")).Should().Be(VoucherStatus.Available);
        (await StatusOf(verify, "instock-warnings")).Should().Be(VoucherStatus.VerifiedWithWarnings);
    }

    [Fact]
    public async Task BookExpiredLoss_WithCostedBatch_SurfacesLossInBatchPnl()
    {
        // Slice 4 → slice 2b bridge: once the nightly job retires the three lapsed-unsold vouchers
        // (3 × 100 L) to Expired, the per-batch P&L must book that stock as an operator loss at the
        // batch cost. No sales are seeded, so the whole net result is the expired loss.
        await SeedAsync(seed =>
        {
            Enable(seed);
            SeedFixture(seed);
            SeedBatchCost(seed, 25m);
        });

        await RunAsync();

        using var verify = CreateContext();
        var rows = await new GetImportBatchPnlQueryHandler(verify)
            .HandleAsync(new GetImportBatchPnlQuery(ImportId));

        var row = rows.Should().ContainSingle().Subject;
        row.FuelTypeId.Should().Be("okko-dp");
        row.CostPerLiter.Should().Be(25m);
        row.VouchersExpired.Should().Be(3);
        row.LitersExpired.Should().Be(300m);
        row.ExpiredLoss.Should().Be(7500m);            // 300 L × 25 UAH/L
        row.RealizedMargin.Should().Be(0m);            // nothing sold
        row.NetRealizedResult.Should().Be(-7500m);     // realized margin − expired loss
    }

    private static async Task<VoucherStatus> StatusOf(ApplicationDbContext context, string number)
        => (await context.FuelVouchers.FirstAsync(v => v.VoucherNumber == number)).Status;

    private static void Enable(ApplicationDbContext seed) => seed.AppSettings.Add(new AppSetting
    {
        Key = AppSettingKeys.ExpiredVoucherLossEnabled,
        Value = "true",
        UpdatedAtUtc = DateTime.UtcNow
    });

    /// <summary>
    /// Costs the vouchers of the (import × okko-dp) batch so the P&amp;L can value expired stock at cost.
    /// Cost lives on the voucher, so the batch row is now just the grouping record.
    /// </summary>
    private static void SeedBatchCost(ApplicationDbContext seed, decimal costPerLiter)
    {
        var now = DateTime.UtcNow;

        var supplierId = seed.Suppliers.Select(s => s.Id).FirstOrDefault();
        if (supplierId == Guid.Empty)
        {
            supplierId = Guid.NewGuid();
            seed.Suppliers.Add(new Supplier
            {
                Id = supplierId,
                Name = "Seed Supplier",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        foreach (var entry in seed.ChangeTracker.Entries<FuelVoucher>())
        {
            if (entry.Entity.ImportJobId == ImportId)
            {
                entry.Entity.CostPerLiter = costPerLiter;
                entry.Entity.SupplierId = supplierId;
            }
        }

        seed.PurchaseBatches.Add(new PurchaseBatch
        {
            Id = Guid.NewGuid(),
            ImportJobId = ImportId,
            FuelTypeId = "okko-dp",
            Provider = "OKKO",
            SupplierId = supplierId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }

    private async Task RunAsync()
    {
        using var context = CreateContext();
        var service = new ExpiredVoucherLossService(
            context, new RuntimeSettingsService(context), NullLogger<ExpiredVoucherLossService>.Instance);
        await service.BookExpiredLossAsync();
    }

    // Vouchers tagged by a stable VoucherNumber discriminator. "instock-*" = operator-owned, sellable and
    // lapsed (must be retired); "future-instock"/"today-instock" = sellable but not past expiry (kept);
    // "lapsed-assigned"/"lapsed-used" = already sold, so the operator loss never applies (kept).
    private static void SeedFixture(ApplicationDbContext seed)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        // fuel_vouchers.import_job_id is a FK to voucher_imports — seed the parent row first
        // (real Postgres enforces it; the InMemory unit tests do not).
        seed.VoucherImports.Add(new VoucherImport
        {
            Id = ImportId,
            FileName = "batch.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow
        });

        // The already-sold vouchers below sit in somebody's hands, so ck_voucher_held_has_order
        // demands the purchase that handed them over behind them.
        var holderId = Guid.NewGuid();
        seed.Users.Add(new User
        {
            Id = holderId,
            PhoneNumber = $"+38{holderId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        var soldOrderId = SeedPurchaseOrder(seed, holderId);

        seed.FuelVouchers.Add(NewVoucher("instock-imported", VoucherStatus.Imported, yesterday));
        seed.FuelVouchers.Add(NewVoucher("instock-available", VoucherStatus.Available, yesterday));
        seed.FuelVouchers.Add(NewVoucher("instock-warnings", VoucherStatus.VerifiedWithWarnings, yesterday));
        seed.FuelVouchers.Add(NewVoucher("future-instock", VoucherStatus.Imported, tomorrow));
        seed.FuelVouchers.Add(NewVoucher("today-instock", VoucherStatus.Imported, today));
        seed.FuelVouchers.Add(NewVoucher("lapsed-assigned", VoucherStatus.Assigned, yesterday, holderId, soldOrderId));
        seed.FuelVouchers.Add(NewVoucher("lapsed-used", VoucherStatus.Used, yesterday, holderId, soldOrderId));
    }

    private static Guid SeedPurchaseOrder(ApplicationDbContext ctx, Guid userId)
    {
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 0,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        return orderId;
    }

    private static FuelVoucher NewVoucher(
        string number,
        VoucherStatus status,
        DateOnly expirationDate,
        Guid? assignedToUserId = null,
        Guid? orderId = null) => new()
    {
        Id = Guid.NewGuid(),
        Provider = "OKKO",
        FuelTypeId = "okko-dp",
        Liters = 100m,
        ProviderExpirationDate = expirationDate,
        CustomerExpirationDate = expirationDate,
        VoucherNumber = number,
        QrPayload = Guid.NewGuid().ToString(),
        Status = status,
        AssignedToUserId = assignedToUserId,
        OrderId = orderId,
        ImportJobId = ImportId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"fuel_vouchers\", \"purchase_batches\", \"voucher_imports\", \"app_settings\" RESTART IDENTITY CASCADE");
        seed(context);
        await context.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
