using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
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
/// when enabled it retires only operator-owned, still-sellable stock whose printed expiration date fell
/// out of the exchange grace window <c>ExpiredVoucherLoss:GraceDays</c> ago, leaving stock still inside
/// the window, future-dated stock, same-day expiries and already-sold (Assigned/Used) vouchers untouched;
/// when disabled it is a pure dry-run that mutates nothing.
/// </summary>
[Collection("Integration Tests")]
public sealed class ExpiredVoucherLossIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    /// <summary>
    /// Comfortably outside the default grace window (14 days), so "lapsed" here means the exchange
    /// window has closed and the loss is real. Deliberately not yesterday: the window is the feature
    /// under test in BookExpiredLoss_RespectsTheGraceWindow, and these fixtures cover what follows it.
    /// </summary>
    private const int LapsedDaysAgo = 30;

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

    /// <summary>
    /// The window itself, proven against a real PostgreSQL <c>date</c> column rather than the
    /// InMemory brand the unit suite uses. The job must retire only stock older than
    /// <c>today - GraceDays</c>; a voucher still inside the window is salvageable, because the operator
    /// can still swap it with the brand for a surcharge (#104) - and once it is Expired the exchange
    /// may no longer accept it, so an early flip destroys the option itself.
    ///
    /// The setting row is deliberately absent: the unset case must fall back to the default window
    /// rather than silently behaving as zero.
    /// </summary>
    [Fact]
    public async Task BookExpiredLoss_RespectsTheGraceWindow_OnRealPostgres()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await SeedAsync(seed =>
        {
            Enable(seed);
            seed.VoucherImports.Add(new VoucherImport
            {
                Id = ImportId,
                FileName = "grace-window.pdf",
                CompletedAtUtc = DateTime.UtcNow,
                Status = "Completed",
                StartedAtUtc = DateTime.UtcNow
            });

            // Default window is 14 days. The boundary is pinned on both sides, because the day it falls
            // on decides whether salvageable stock is written off.
            seed.FuelVouchers.Add(NewVoucher("window-inside", VoucherStatus.Available, today.AddDays(-3)));
            seed.FuelVouchers.Add(NewVoucher("window-boundary", VoucherStatus.Available, today.AddDays(-14)));
            seed.FuelVouchers.Add(NewVoucher("window-outside", VoucherStatus.Available, today.AddDays(-15)));
        });

        await RunAsync();

        using var verify = CreateContext();
        (await StatusOf(verify, "window-inside")).Should().Be(VoucherStatus.Available);
        (await StatusOf(verify, "window-boundary")).Should().Be(VoucherStatus.Available);
        (await StatusOf(verify, "window-outside")).Should().Be(VoucherStatus.Expired);
    }

    /// <summary>
    /// D-13, eighth writer — and the first one with no person behind it. Every other recorded action is
    /// a consequence of an order somebody placed, so <c>actor_kind = 'user'</c> was the only value any
    /// writer had ever used, and <c>'system'</c> existed as a CHECK-constrained value that nothing wrote.
    /// This is the first row to use it.
    /// <para>
    /// It runs against real PostgreSQL deliberately. The whole reason the value is legal is the pairing
    /// CHECK — <c>(actor_kind = 'user') = (actor_user_id IS NOT NULL)</c> — which admits <c>'system'</c>
    /// precisely because its id is NULL. The InMemory brand has no CHECK constraints and would happily
    /// accept a row the production database rejects, so proving <c>'system'</c> works here is the point.
    /// </para>
    /// <para>
    /// One event per retired voucher, each carrying the status that voucher actually had. The candidate
    /// set spans <c>Imported</c>, <c>VerifiedWithWarnings</c> and <c>Available</c>, so a single hardcoded
    /// from_status would tell the history that every stock expiry began at <c>Available</c> — false for
    /// imported-but-never-activated stock, which is precisely the kind of voucher this job exists for.
    /// </para>
    /// </summary>
    [Fact]
    public async Task BookExpiredLoss_RecordsASystemAuthoredEvent_SayingTheStatusItRetiredFrom()
    {
        await SeedAsync(seed =>
        {
            Enable(seed);
            SeedFixture(seed);
        });

        await RunAsync();

        using var verify = CreateContext();

        var events = await verify.VoucherEvents.AsNoTracking().ToListAsync();
        events.Should().HaveCount(3, "one event per voucher the job actually retired");

        events.Should().OnlyContain(e => e.EventType == VoucherEventTypes.StatusChanged);
        events.Should().OnlyContain(e => e.ToStatus == nameof(VoucherStatus.Expired));
        events.Should().OnlyContain(e => e.ActorKind == VoucherActorKinds.System);
        events.Should().OnlyContain(e => e.ActorUserId == null,
            "a scheduler has nobody to name, and the pairing CHECK admits 'system' only with a NULL id");
        events.Should().OnlyContain(e => e.Reason != null && e.Reason.Contains("nightly loss sweep"),
            "the grace window is why this happened today and not on the printed date");

        async Task<string?> FromOf(string number)
        {
            var voucherId = await verify.FuelVouchers.AsNoTracking()
                .Where(v => v.VoucherNumber == number)
                .Select(v => v.Id)
                .SingleAsync();
            return events.Single(e => e.VoucherId == voucherId).FromStatus;
        }

        (await FromOf("instock-imported")).Should().Be(nameof(VoucherStatus.Imported));
        (await FromOf("instock-warnings")).Should().Be(nameof(VoucherStatus.VerifiedWithWarnings));
        (await FromOf("instock-available")).Should().Be(nameof(VoucherStatus.Available));

        // Nothing that was kept may be recorded as retired. A voucher still inside the grace window is
        // salvageable, and an event claiming it was written off would contradict the stock it still is.
        foreach (var kept in new[] { "future-instock", "today-instock", "window-inside" })
        {
            var voucherId = await verify.FuelVouchers.AsNoTracking()
                .Where(v => v.VoucherNumber == kept)
                .Select(v => v.Id)
                .FirstOrDefaultAsync();
            if (voucherId != Guid.Empty)
            {
                events.Should().NotContain(e => e.VoucherId == voucherId);
            }
        }
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

        // D-13: a dry run that wrote events would book a write-off every night while the setting is off -
        // and this job is off by default, so that would be the default behaviour. The log says what
        // *would* be retired; the ledger must say nothing until it actually is.
        (await verify.VoucherEvents.CountAsync()).Should().Be(0,
            "the job only books a loss when enabled, so a dry run records no retirement");
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
            Brand = "OKKO",
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
    //
    // "Lapsed" means older than the loss grace window (ExpiredVoucherLoss:GraceDays, default 14), not
    // merely yesterday: inside that window the operator can still swap the voucher for a surcharge, so
    // the loss is not yet realised and the job must leave it alone. BookExpiredLoss_RespectsTheGraceWindow
    // pins the window itself; these fixtures pin what happens once it has closed.
    private static void SeedFixture(ApplicationDbContext seed)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lapsed = today.AddDays(-LapsedDaysAgo);
        var tomorrow = today.AddDays(1);

        // fuel_vouchers.import_job_id is a FK to voucher_imports — seed the parent row first
        // (real Postgres enforces it; the InMemory unit tests do not).
        seed.VoucherImports.Add(new VoucherImport
        {
            Id = ImportId,
            FileName = "batch.pdf",
            CompletedAtUtc = DateTime.UtcNow,
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

        seed.FuelVouchers.Add(NewVoucher("instock-imported", VoucherStatus.Imported, lapsed));
        seed.FuelVouchers.Add(NewVoucher("instock-available", VoucherStatus.Available, lapsed));
        seed.FuelVouchers.Add(NewVoucher("instock-warnings", VoucherStatus.VerifiedWithWarnings, lapsed));
        seed.FuelVouchers.Add(NewVoucher("future-instock", VoucherStatus.Imported, tomorrow));
        seed.FuelVouchers.Add(NewVoucher("today-instock", VoucherStatus.Imported, today));
        seed.FuelVouchers.Add(NewVoucher("lapsed-assigned", VoucherStatus.Assigned, lapsed, holderId, soldOrderId));
        seed.FuelVouchers.Add(NewVoucher("lapsed-used", VoucherStatus.Used, lapsed, holderId, soldOrderId));
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
        Brand = "OKKO",
        FuelTypeId = "okko-dp",
        Liters = 100m,
        SupplierExpirationDate = expirationDate,
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
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
