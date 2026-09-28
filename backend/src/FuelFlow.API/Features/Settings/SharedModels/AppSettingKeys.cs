namespace FuelFlow.Features.Settings.SharedModels;

public static class AppSettingKeys
{
    /// <summary>
    /// Whether the fulfillment pipeline may automatically refund a partially fulfilled order
    /// after the grace period elapses. Defaults to false.
    /// </summary>
    public const string AutoRefundEnabled = "AutoRefund:Enabled";

    /// <summary>
    /// Grace period in whole days an order must have been partially fulfilled before the
    /// auto-refund may trigger (e.g. 7 = a week, 30 = a month, 365 = a year). Defaults to 7.
    /// </summary>
    public const string AutoRefundDelayDays = "AutoRefund:DelayDays";

    /// <summary>
    /// Master server-side switch for QA App-Store test-account sign-in. When "true" (and a QA code
    /// is configured), the seeded QA phone can authenticate with the configured QA code; any other
    /// value, a missing/malformed row, or an unreadable table all resolve to disabled. This is the
    /// authoritative control — the mobile client never decides it. Default: false (fail-safe).
    /// </summary>
    public const string QaTestAccessEnabled = "QaTestAccess:Enabled";

    /// <summary>
    /// Whether the background job may permanently (hard-)delete abandoned orders — those a customer
    /// soft-deleted (<c>IsDeleted=true</c>) that reconciliation has since driven to <c>Cancelled</c> —
    /// once they age past the retention window. Irreversible, so it defaults to false: an admin opts
    /// in explicitly. See <see cref="OrderCleanupRetentionDays"/>.
    /// </summary>
    public const string OrderCleanupEnabled = "OrderCleanup:Enabled";

    /// <summary>
    /// How many whole days an abandoned (soft-deleted + <c>Cancelled</c>) order must have sat
    /// untouched before the cleanup job may purge it. Keeps rows around for audit before deletion.
    /// Defaults to 30.
    /// </summary>
    public const string OrderCleanupRetentionDays = "OrderCleanup:RetentionDays";
}
