using FluentAssertions;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// A redeploy used to repeat every alert that was still open: the throttle was a process-static
/// dictionary, so the first sweep after a restart found it empty and re-sent everything. Production
/// showed it on 2026-10-30 - four stock alerts a few minutes after the 10:41 deploy, for shortages
/// that had already been reported at 08:10.
/// </summary>
public sealed class NotificationThrottleTests
{
    [Fact]
    public async Task ASecondProcess_SharingTheStore_DoesNotRepeatTheAlert()
    {
        var store = new InMemoryCacheService(NullLogger<InMemoryCacheService>.Instance);
        var alerts = new RecordingNotifier();
        var brand = UniqueBrand();

        await SendLowStockAsync(alerts, store, brand);
        // A restarted process: a new dispatcher and an empty in-process dictionary, the same store.
        await SendLowStockAsync(alerts, store, brand);

        alerts.Sent.Should().ContainSingle("the second process sees the same incident, not a new one");
    }

    [Fact]
    public async Task ASecondProcess_WithAReachableStoreButADifferentKey_StillAlerts()
    {
        // The store must not turn the throttle into a mute: another shortage is another message.
        var store = new InMemoryCacheService(NullLogger<InMemoryCacheService>.Instance);
        var alerts = new RecordingNotifier();
        var brand = UniqueBrand();

        await SendLowStockAsync(alerts, store, brand);
        await SendLowStockAsync(alerts, store, brand, fuelType: "А-95");

        alerts.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhenTheStoreFails_TheAlertStillGoes()
    {
        // Alerting outranks the throttle: an unreachable cache may cost a repeated message, never
        // a missed one.
        var alerts = new RecordingNotifier();

        await SendLowStockAsync(alerts, new UnreachableStore(), UniqueBrand());

        alerts.Sent.Should().ContainSingle();
    }

    private static string UniqueBrand() => $"okko-{Guid.NewGuid().ToString()[..6]}";

    private static Task SendLowStockAsync(
        RecordingNotifier alerts,
        ICacheService? store,
        string brand,
        string fuelType = "ДП")
    {
        var telegram = Options.Create(new TelegramOptions
        {
            Notifications = new TelegramOptions.NotificationOptions
            {
                Vouchers = new TelegramOptions.VoucherNotificationOptions
                {
                    NotifyOnLowStock = true,
                    ReminderIntervalMinutes = 360
                }
            }
        });

        var dispatcher = new NotificationDispatcher(
            alerts,
            telegram,
            NullLogger<NotificationDispatcher>.Instance,
            store);

        return dispatcher.VoucherStockLowAsync(brand, fuelType, liters: 10, available: 3, threshold: 20);
    }

    private sealed class RecordingNotifier : IAlertNotifier
    {
        public List<string> Sent { get; } = [];

        public Task SendAsync(
            AlertSeverity severity,
            string title,
            string message,
            IReadOnlyDictionary<string, string>? context = null,
            bool respectMinimumSeverity = true,
            CancellationToken ct = default)
        {
            Sent.Add(title);
            return Task.CompletedTask;
        }
    }

    private sealed class UnreachableStore : ICacheService
    {
        public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("cache is down");

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("cache is down");

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("cache is down");

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("cache is down");

        public Task<string?> GetAndDeleteAsync(string key, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("cache is down");

        public Task<bool> TrySetIfAbsentAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("cache is down");
    }
}
