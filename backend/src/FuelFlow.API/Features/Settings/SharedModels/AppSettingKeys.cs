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
}
