namespace FuelFlow.Features.Vouchers.Renewal;

/// <summary>Manager-configured state of one renewal tier.</summary>
public sealed record VoucherRenewalTierConfig(VoucherRenewalTerm Term, bool Enabled, decimal RatePerLiterUah)
{
    /// <summary>
    /// A tier can be sold only when the manager has both enabled it and set a positive UAH-per-litre
    /// rate. Fail-safe: no price ⇒ not offerable, so we never charge 0 for a renewal. (Stock
    /// availability is a further, per-branch gate applied at quote time; this is config-only.)
    /// </summary>
    public bool IsOfferable => Enabled && RatePerLiterUah > 0m;
}

/// <summary>
/// The whole renewal-feature configuration, read from <c>app_settings</c>. Missing rows resolve to
/// fail-safe defaults: feature off, 14-day threshold, every tier off with a zero rate.
/// </summary>
public sealed record VoucherRenewalConfig(
    bool Enabled,
    int TriggerThresholdDays,
    IReadOnlyList<VoucherRenewalTierConfig> Tiers)
{
    public VoucherRenewalTierConfig? Tier(VoucherRenewalTerm term)
        => Tiers.FirstOrDefault(t => t.Term == term);
}
