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

    /// <summary>
    /// Master switch for the nightly data-retention job (<c>DataRetentionService</c>), which prunes
    /// high-churn operational rows — spent OTPs, dead refresh tokens, processed outbox events, read
    /// notifications, aged error logs, stale push tokens — each past its own fixed window. It never
    /// touches live data (a valid token or an unread notification is always excluded by predicate).
    /// Defaults to false: while off the job runs read-only, logging how many rows *would* be purged
    /// so an admin can see the impact before opting in. Order cleanup is a separate switch
    /// (<see cref="OrderCleanupEnabled"/>); this job deliberately excludes orders.
    /// </summary>
    public const string DataRetentionEnabled = "DataRetention:Enabled";

    /// <summary>
    /// Master switch for the nightly expired-voucher loss-booking job (<c>ExpiredVoucherLossService</c>),
    /// which retires operator-unsold vouchers that have lapsed past their expiration date to
    /// <c>Expired</c> — recognising their cost as a realised loss so per-batch P&amp;L stops counting them
    /// as sellable stock. Fail-safe: defaults to false, so while off the job runs read-only, logging the
    /// loss that *would* be booked so an admin can gauge the impact before opting in. Only once enabled
    /// does it mutate voucher status.
    /// </summary>
    public const string ExpiredVoucherLossEnabled = "ExpiredVoucherLoss:Enabled";

    /// <summary>
    /// Master switch for the paid voucher renewal/replacement flow. Fail-safe: defaults to false, so
    /// the mobile button never appears and no renewal order can be created until a manager has both
    /// turned it on and configured tier prices. See the per-tier keys below.
    /// </summary>
    public const string VoucherRenewalEnabled = "VoucherRenewal:Enabled";

    /// <summary>
    /// How many days of remaining validity (or fewer) make a voucher surface the "renew / replace"
    /// button. Already-expired vouchers always qualify. Defaults to 14 (two weeks).
    /// </summary>
    public const string VoucherRenewalTriggerThresholdDays = "VoucherRenewal:TriggerThresholdDays";

    /// <summary>Common prefix for every renewal setting; used to load the whole config in one query.</summary>
    public const string VoucherRenewalPrefix = "VoucherRenewal:";

    /// <summary>
    /// Per-tier on/off flag key, e.g. <c>VoucherRenewal:Tier:3m:Enabled</c>. <paramref name="termCode"/>
    /// is the tier's <c>VoucherRenewalTerm.Code()</c> (1w/2w/1m…6m).
    /// </summary>
    public static string VoucherRenewalTierEnabled(string termCode)
        => $"{VoucherRenewalPrefix}Tier:{termCode}:Enabled";

    /// <summary>Per-tier UAH-per-litre rate key, e.g. <c>VoucherRenewal:Tier:3m:RatePerLiter</c>.</summary>
    public static string VoucherRenewalTierRatePerLiter(string termCode)
        => $"{VoucherRenewalPrefix}Tier:{termCode}:RatePerLiter";
}
