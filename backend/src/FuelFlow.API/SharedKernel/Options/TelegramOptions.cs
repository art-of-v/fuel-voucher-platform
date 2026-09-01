namespace FuelFlow.SharedKernel.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>
    /// Disabled by default so local dev, tests and CI never attempt network calls.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Bot token from @BotFather. Bearer credential - supply via environment variable
    /// (Telegram__BotToken) or user-secrets, never a committed file.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Target chat IDs. Prefer a single private group over per-person DMs: adding a
    /// recipient then becomes a Telegram action rather than a config change + redeploy.
    /// Group IDs are negative (e.g. "-1001234567890").
    /// </summary>
    public List<string> ChatIds { get; set; } = [];

    /// <summary>
    /// Lowest severity that triggers a send. One of: Info, Warning, Critical.
    /// </summary>
    public string MinimumSeverity { get; set; } = "Warning";

    public string BaseUrl { get; set; } = "https://api.telegram.org";

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Opt-in switches for application-raised notifications. These are distinct from
    /// the Grafana alerts driven by Prometheus rules: these fire on a domain event the
    /// instant it happens, whereas Grafana alerts describe a condition that has held
    /// for some time.
    /// </summary>
    public NotificationOptions Notifications { get; set; } = new();

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(BotToken)
        && ChatIds.Exists(id => !string.IsNullOrWhiteSpace(id));

    public sealed class NotificationOptions
    {
        /// <summary>
        /// Sends a message when a phone number is verified for the first time and a
        /// User row is created. Off by default: on a consumer app this can be a high
        /// volume of messages, so enable it deliberately.
        /// </summary>
        public bool NotifyOnNewUser { get; set; } = false;

        /// <summary>
        /// Sends a message when a legal entity (company) profile is created. Updates to
        /// an existing profile are not reported - only the first creation.
        /// </summary>
        public bool NotifyOnCompanyCreated { get; set; } = false;

        /// <summary>
        /// Sends a message when a Monobank payment is confirmed as successful.
        /// High volume on a busy day; off by default.
        /// </summary>
        public bool NotifyOnPaymentSuccess { get; set; } = false;

        /// <summary>
        /// Sends a message when a payment fails or is reversed.
        /// </summary>
        public bool NotifyOnPaymentFailure { get; set; } = false;

        /// <summary>
        /// Sends a message when a voucher import finishes with at least one rejected
        /// row (parse exception, failed validation, or a failed QR integrity check).
        /// One summary message per import, not one per bad row.
        /// </summary>
        public bool NotifyOnImportErrors { get; set; } = false;

        /// <summary>
        /// Sends a message for any unhandled exception that reaches the global handler.
        /// Noisy by nature - a single broken endpoint hit by a client retry loop can
        /// flood the chat, which is why <see cref="ExceptionThrottleMinutes"/> exists.
        /// </summary>
        public bool NotifyOnUnhandledException { get; set; } = false;

        /// <summary>
        /// Minimum gap between messages for the same exception type + endpoint pair.
        /// </summary>
        public int ExceptionThrottleMinutes { get; set; } = 15;

        public VoucherNotificationOptions Vouchers { get; set; } = new();
    }

    public sealed class VoucherNotificationOptions
    {
        /// <summary>
        /// Sends a message when the available pool for a provider/fuel-type combination
        /// drops to or below <see cref="LowLevelThreshold"/>.
        /// </summary>
        public bool NotifyOnLowStock { get; set; } = false;

        /// <summary>
        /// Sends a separate, higher-severity message when a provider/fuel-type
        /// combination reaches zero. Kept distinct from <see cref="NotifyOnLowStock"/>
        /// so the "warning" and the "we are out" cases can be routed independently.
        /// </summary>
        public bool NotifyOnZeroStock { get; set; } = false;

        /// <summary>
        /// Sends a message when an order cannot be fulfilled because no matching
        /// voucher is available. This is the business-critical case: a paying customer
        /// is already waiting, so it is reported at Critical severity.
        /// </summary>
        public bool NotifyOnOrderUnfulfillable { get; set; } = false;

        /// <summary>
        /// Evaluated per provider/fuel-type combination rather than on the total, so an
        /// exhausted single combination is not hidden by a healthy overall count.
        /// </summary>
        public int LowLevelThreshold { get; set; } = 20;

        /// <summary>
        /// Minimum gap between messages for the same provider/fuel-type combination.
        /// The check runs on a schedule, so without this a persistent shortage would
        /// repeat every run until someone tops the pool up.
        /// </summary>
        public int ReminderIntervalMinutes { get; set; } = 360;
    }
}
