using System.Collections.Concurrent;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Single place where domain events are turned into alerts. Call sites raise an
/// intent ("a payment failed") and this decides whether it is enabled, at what
/// severity, and how it is worded.
/// <para>
/// Keeping the config checks here rather than at each call site means a new toggle
/// never requires touching a handler, and the toggles cannot drift apart in how they
/// are interpreted. Every method swallows its own errors: alerting must never fail
/// the request or job that triggered it.
/// </para>
/// </summary>
public sealed class NotificationDispatcher
{
    /// <summary>
    /// Last send time per throttle key. Static and process-local: a restart
    /// intentionally re-arms everything, and losing the history is harmless because
    /// the worst case is one extra message.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime> LastSentUtc = new();

    private readonly IAlertNotifier _alerts;
    private readonly TelegramOptions.NotificationOptions _config;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IAlertNotifier alerts,
        IOptions<TelegramOptions> telegram,
        ILogger<NotificationDispatcher> logger)
    {
        _alerts = alerts;
        _config = telegram.Value.Notifications;
        _logger = logger;
    }

    /// <summary>
    /// A dispatcher with every notification switched off. For tests and any other
    /// caller that constructs a service directly rather than through DI, so they do
    /// not have to build a notifier and options just to be ignored.
    /// </summary>
    public static NotificationDispatcher Disabled { get; } = new(
        new NullAlertNotifier(),
        Microsoft.Extensions.Options.Options.Create(new TelegramOptions()),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationDispatcher>.Instance);

    public Task NewUserRegisteredAsync(Guid userId, string phoneNumber, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnNewUser,
            AlertSeverity.Info,
            "New user registered",
            "A phone number was verified for the first time.",
            new Dictionary<string, string>
            {
                ["User ID"] = userId.ToString(),
                ["Phone"] = MaskPhoneNumber(phoneNumber)
            },
            throttleKey: null,
            ct);

    public Task CompanyCreatedAsync(Guid legalEntityId, string name, string edrpou, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnCompanyCreated,
            AlertSeverity.Info,
            "Company profile created",
            $"A legal entity profile was created for {name}.",
            new Dictionary<string, string>
            {
                ["Legal entity ID"] = legalEntityId.ToString(),
                ["Name"] = name,
                ["EDRPOU"] = edrpou
            },
            throttleKey: null,
            ct);

    public Task PaymentSucceededAsync(Guid orderId, decimal amount, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnPaymentSuccess,
            AlertSeverity.Info,
            "Payment successful",
            $"Payment confirmed for order {orderId}.",
            new Dictionary<string, string>
            {
                ["Order ID"] = orderId.ToString(),
                ["Amount"] = amount.ToString("0.00")
            },
            throttleKey: null,
            ct);

    public Task PaymentFailedAsync(Guid orderId, string status, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnPaymentFailure,
            AlertSeverity.Warning,
            "Payment failed",
            $"Payment for order {orderId} did not complete.",
            new Dictionary<string, string>
            {
                ["Order ID"] = orderId.ToString(),
                ["Status"] = status
            },
            throttleKey: null,
            ct);

    /// <summary>
    /// One summary per import rather than one message per bad row: a malformed batch
    /// of several hundred vouchers would otherwise be unusable as a notification.
    /// </summary>
    public Task ImportCompletedWithErrorsAsync(
        int importedCount,
        int errorCount,
        IReadOnlyList<string> sampleErrors,
        CancellationToken ct = default)
    {
        var context = new Dictionary<string, string>
        {
            ["Imported"] = importedCount.ToString(),
            ["Errors"] = errorCount.ToString()
        };

        for (var i = 0; i < sampleErrors.Count && i < 5; i++)
        {
            context[$"Error {i + 1}"] = Truncate(sampleErrors[i], 200);
        }

        if (errorCount > sampleErrors.Count)
        {
            context["Note"] = $"{errorCount - sampleErrors.Count} further error(s) not shown";
        }

        return SendAsync(
            _config.NotifyOnImportErrors && errorCount > 0,
            AlertSeverity.Warning,
            "Voucher import completed with errors",
            $"{errorCount} voucher(s) were rejected during import.",
            context,
            throttleKey: null,
            ct);
    }

    public Task VoucherStockLowAsync(
        string provider,
        string fuelType,
        int available,
        int threshold,
        CancellationToken ct = default)
    {
        var vouchers = _config.Vouchers;
        var isZero = available == 0;
        var enabled = isZero ? vouchers.NotifyOnZeroStock : vouchers.NotifyOnLowStock;

        return SendAsync(
            enabled,
            isZero ? AlertSeverity.Critical : AlertSeverity.Warning,
            isZero ? "Voucher stock exhausted" : "Voucher stock low",
            isZero
                ? $"There are no vouchers left for {provider}."
                : $"Only {available} voucher(s) left for {provider}.",
            new Dictionary<string, string>
            {
                ["Provider"] = provider,
                ["Fuel type"] = fuelType,
                ["Available"] = available.ToString(),
                ["Threshold"] = threshold.ToString()
            },
            throttleKey: $"stock|{provider}|{fuelType}|{isZero}",
            ct,
            throttleMinutes: vouchers.ReminderIntervalMinutes);
    }

    /// <summary>
    /// Raised when a paid order cannot be filled because no matching voucher exists.
    /// Critical rather than Warning: a customer has already been charged and is
    /// waiting, so this needs someone to act rather than just be recorded.
    /// </summary>
    public Task OrderUnfulfillableAsync(
        Guid orderId,
        string fuelType,
        int assigned,
        int needed,
        CancellationToken ct = default)
        => SendAsync(
            _config.Vouchers.NotifyOnOrderUnfulfillable,
            AlertSeverity.Critical,
            "Order cannot be fulfilled - no vouchers",
            $"Order {orderId} is short of vouchers and cannot be completed.",
            new Dictionary<string, string>
            {
                ["Order ID"] = orderId.ToString(),
                ["Fuel type"] = fuelType,
                ["Assigned"] = assigned.ToString(),
                ["Needed"] = needed.ToString()
            },
            throttleKey: $"unfulfillable|{orderId}|{fuelType}",
            ct,
            throttleMinutes: 60);

    /// <summary>
    /// Throttled per exception type + endpoint, because a single broken endpoint under
    /// a client retry loop would otherwise flood the chat and hide everything else.
    /// </summary>
    public Task UnhandledExceptionAsync(Exception exception, string endpoint, CancellationToken ct = default)
        => SendAsync(
            _config.NotifyOnUnhandledException,
            AlertSeverity.Critical,
            "Unhandled exception",
            Truncate(exception.Message, 500),
            new Dictionary<string, string>
            {
                ["Type"] = exception.GetType().Name,
                ["Endpoint"] = endpoint
            },
            throttleKey: $"ex|{exception.GetType().FullName}|{endpoint}",
            ct,
            throttleMinutes: _config.ExceptionThrottleMinutes);

    private async Task SendAsync(
        bool enabled,
        AlertSeverity severity,
        string title,
        string message,
        IReadOnlyDictionary<string, string> context,
        string? throttleKey,
        CancellationToken ct,
        int throttleMinutes = 0)
    {
        if (!enabled)
        {
            return;
        }

        if (throttleKey is not null && throttleMinutes > 0 && !TryClaimSlot(throttleKey, throttleMinutes))
        {
            return;
        }

        try
        {
            // respectMinimumSeverity: false - the per-event toggle above is the gate.
            // Otherwise an Info-level event such as "new user" would be silently
            // dropped by the default Warning floor despite being explicitly enabled.
            await _alerts.SendAsync(severity, title, message, context, respectMinimumSeverity: false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch notification {Title}", title);
        }
    }

    private static bool TryClaimSlot(string key, int throttleMinutes)
    {
        var now = DateTime.UtcNow;
        var interval = TimeSpan.FromMinutes(throttleMinutes);

        var updated = LastSentUtc.AddOrUpdate(
            key,
            now,
            (_, previous) => now - previous >= interval ? now : previous);

        return updated == now;
    }

    /// <summary>
    /// Keeps the last four digits only: enough to correlate with a support request
    /// without putting a full personal phone number into a chat group.
    /// </summary>
    private static string MaskPhoneNumber(string phoneNumber)
        => phoneNumber.Length <= 4 ? "****" : $"****{phoneNumber[^4..]}";

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
