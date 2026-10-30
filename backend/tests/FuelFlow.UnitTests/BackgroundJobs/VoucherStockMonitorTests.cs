using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// The pool monitor used to alert only on a count of <c>Available</c> rows. That number is blind to
/// the case that actually costs money: a voucher that is still Available but days from its expiry.
/// It counts as stock, the pool reads healthy, and on its expiry date it leaves the Available set
/// and becomes <c>Expired</c> — worthless, with no message having been sent at any point.
///
/// These tests seed exactly that shape: a comfortably healthy pool whose stock is nearly out of life.
/// </summary>
public sealed class VoucherStockMonitorTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RecordingAlertNotifier _alerts;

    public VoucherStockMonitorTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options, TestQrCipher.Instance);
        _context.Database.EnsureCreated();
        _alerts = new RecordingAlertNotifier();

        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("test-expiring-dp"),
            Name = "ДП ЄВРО",
            BrandId = BrandIds.Okko,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>
    /// These dispatchers are built without a cache, so the throttle falls back to the
    /// process-static dictionary keyed on brand+fuel, and two tests that alert on the same
    /// combination would silently suppress each other within the reminder interval. Each test
    /// therefore gets its own brand. (The cache-backed throttle is covered by
    /// <c>NotificationThrottleTests</c>.)
    /// </summary>
    private readonly string _provider = $"OKKO-{Guid.NewGuid().ToString()[..6]}";

    private void Voucher(
        int daysUntilExpiry,
        VoucherStatus status = VoucherStatus.Available,
        string? brand = null,
        decimal liters = 10,
        string dataKind = DataKinds.Real)
        => _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug(brand ?? _provider),
            Brand = brand ?? _provider,
            FuelTypeId = ToUuid("test-expiring-dp"),
            Liters = liters,
            DataKind = dataKind,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddDays(daysUntilExpiry)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddDays(daysUntilExpiry)),
            VoucherNumber = $"{brand}-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

    private Task RunAsync(Action<TelegramOptions.VoucherNotificationOptions> configure)
    {
        var vouchers = new TelegramOptions.VoucherNotificationOptions { LowLevelThreshold = 5 };
        configure(vouchers);

        // One options object for both: the monitor reads the switches to decide whether to look at
        // anything at all, the dispatcher reads the same switches to decide whether to send. Wiring
        // them to different instances is how a test ends up passing against nothing.
        var telegram = Options.Create(new TelegramOptions
        {
            Notifications = new TelegramOptions.NotificationOptions { Vouchers = vouchers }
        });

        return new VoucherStockMonitor(
            _context,
            new NotificationDispatcher(_alerts, telegram, NullLogger<NotificationDispatcher>.Instance),
            telegram,
            NullLogger<VoucherStockMonitor>.Instance
        ).CheckLowStockAsync();
    }

    [Fact]
    public async Task Alerts_WhenStockIsHealthyButNearlyOutOfLife()
    {
        // 50 available, all expiring within the horizon: the pool count says "healthy" and the
        // old monitor said nothing at all.
        for (var i = 0; i < 50; i++) Voucher(daysUntilExpiry: 3);
        await _context.SaveChangesAsync();

        await RunAsync(v => v.NotifyOnExpiringStock = true);

        _alerts.Sent.Should().ContainSingle(a => a.Title == "Талони добігають терміну");
        var alert = _alerts.Sent.Single(a => a.Title == "Талони добігають терміну");
        alert.Context!["Добігають терміну"].Should().Be("50");
        alert.Context["Доступно"].Should().Be("50");
        alert.Severity.Should().Be(AlertSeverity.Warning);
    }

    [Fact]
    public async Task Silent_WhenNothingIsExpiring()
    {
        for (var i = 0; i < 50; i++) Voucher(daysUntilExpiry: 60);
        await _context.SaveChangesAsync();

        await RunAsync(v => v.NotifyOnExpiringStock = true);

        _alerts.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpiredStockIsNotReportedAsExpiring()
    {
        // Already Expired is not "about to expire" — it is already gone, and the expired-loss job
        // books it. Reporting it here would double-count the same fuel in two alerts.
        Voucher(daysUntilExpiry: -5, status: VoucherStatus.Expired);
        await _context.SaveChangesAsync();

        await RunAsync(v => v.NotifyOnExpiringStock = true);

        _alerts.Sent.Should().NotContain(a => a.Title == "Талони добігають терміну");
    }

    [Fact]
    public async Task AssignedStockIsNotReportedAsExpiring()
    {
        // Already in a customer's hands — that is their problem now, not stock we are about to lose.
        Voucher(daysUntilExpiry: 2, status: VoucherStatus.Assigned);
        await _context.SaveChangesAsync();
        await _context.SaveChangesAsync();

        await RunAsync(v => v.NotifyOnExpiringStock = true);

        _alerts.Sent.Should().NotContain(a => a.Title == "Талони добігають терміну");
    }

    [Fact]
    public async Task RespectsTheConfiguredHorizon()
    {
        Voucher(daysUntilExpiry: 20);
        await _context.SaveChangesAsync();

        // 14-day default would stay quiet about a voucher 20 days out.
        await RunAsync(v =>
        {
            v.NotifyOnExpiringStock = true;
            v.ExpiringWithinDays = 30;
        });

        _alerts.Sent.Should().ContainSingle(a => a.Title == "Талони добігають терміну");
    }

    [Fact]
    public async Task LowStockAlertStillFires_AndExpiringIsASeparateMessage()
    {
        // The two cases can be true at once and deserve a message each: one says "top the pool up",
        // the other says "this stock is about to be worth nothing".
        Voucher(daysUntilExpiry: 2);
        Voucher(daysUntilExpiry: 90);
        await _context.SaveChangesAsync();

        await RunAsync(v =>
        {
            v.NotifyOnExpiringStock = true;
            v.NotifyOnLowStock = true;
        });

        _alerts.Sent.Should().Contain(a => a.Title == "Талони добігають терміну");
        _alerts.Sent.Should().Contain(a => a.Title == "Малий залишок талонів");
    }

    [Fact]
    public async Task QaStockIsNotCountedAsASellablePool()
    {
        // Rehearsal vouchers are not fuel a customer can be handed, so they must not prop up the
        // count the operator is asked to top up: five real vouchers is a pool of five (planning #781).
        for (var i = 0; i < 5; i++) Voucher(daysUntilExpiry: 90);
        for (var i = 0; i < 99; i++) Voucher(daysUntilExpiry: 90, dataKind: DataKinds.Qa);
        await _context.SaveChangesAsync();

        await RunAsync(v => v.NotifyOnLowStock = true);

        var alert = _alerts.Sent.Should().ContainSingle(a => a.Title == "Малий залишок талонів").Which;
        alert.Context!["Доступно"].Should().Be("5",
            "ninety-nine emulated vouchers are not stock the customer at the counter can buy");
    }

    [Fact]
    public async Task APackSizeIsItsOwnPool()
    {
        // A deep 20 L pool does not cover a 10 L line: fulfilment matches the size too, so the
        // shortage has to be reported for the size that is short (planning #781).
        for (var i = 0; i < 50; i++) Voucher(daysUntilExpiry: 90, liters: 20);
        Voucher(daysUntilExpiry: 90, liters: 10);
        await _context.SaveChangesAsync();

        await RunAsync(v => v.NotifyOnLowStock = true);

        var alert = _alerts.Sent.Should().ContainSingle(a => a.Title == "Малий залишок талонів").Which;
        alert.Context!["Фасування"].Should().Be("10 л");
        alert.Context["Доступно"].Should().Be("1");
    }

    [Fact]
    public async Task TheThresholdIsPerCombination_NotForTheirTotal()
    {
        // Fifteen of each size totals thirty, well above the threshold - and still two shortages,
        // because neither size can serve the other's order.
        for (var i = 0; i < 15; i++) Voucher(daysUntilExpiry: 90, liters: 10);
        for (var i = 0; i < 15; i++) Voucher(daysUntilExpiry: 90, liters: 20);
        await _context.SaveChangesAsync();

        await RunAsync(v =>
        {
            v.NotifyOnLowStock = true;
            v.LowLevelThreshold = 20;
        });

        _alerts.Sent.Where(a => a.Title == "Малий залишок талонів").Should().HaveCount(2,
            "a healthy total must not hide a size that is running out");
    }

    [Fact]
    public async Task ExpiringAlertRespectsItsOwnSwitch()
    {
        for (var i = 0; i < 50; i++) Voucher(daysUntilExpiry: 3);
        await _context.SaveChangesAsync();

        // Off by default, like every other notification flag in this file.
        await RunAsync(v => v.NotifyOnExpiringStock = false);

        _alerts.Sent.Should().BeEmpty();
    }

    private sealed class RecordingAlertNotifier : IAlertNotifier
    {
        public List<(AlertSeverity Severity, string Title, string Message, IReadOnlyDictionary<string, string>? Context)> Sent { get; } = [];

        public Task SendAsync(
            AlertSeverity severity,
            string title,
            string message,
            IReadOnlyDictionary<string, string>? context = null,
            bool respectMinimumSeverity = true,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((severity, title, message, context));
            return Task.CompletedTask;
        }
    }
}