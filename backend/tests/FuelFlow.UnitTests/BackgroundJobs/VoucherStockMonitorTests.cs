using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

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
                .Options);
        _context.Database.EnsureCreated();
        _alerts = new RecordingAlertNotifier();

        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "test-expiring-dp",
            Name = "ДП ЄВРО",
            StationId = "okko",
            BasePrice = 100,
            DiscountPrice = 95,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>
    /// The dispatcher's throttle is a process-static dictionary keyed on provider+fuel, so two
    /// tests that alert on the same combination would silently suppress each other within the
    /// reminder interval. Each test therefore gets its own brand.
    /// </summary>
    private readonly string _provider = $"OKKO-{Guid.NewGuid().ToString()[..6]}";

    private void Voucher(int daysUntilExpiry, VoucherStatus status = VoucherStatus.Available, string? provider = null)
        => _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = provider ?? _provider,
            FuelTypeId = "test-expiring-dp",
            Liters = 10,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysUntilExpiry)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysUntilExpiry)),
            VoucherNumber = $"{provider}-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
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