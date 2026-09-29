namespace FuelFlow.Features.Vouchers.Renewal.Quote;

/// <summary>
/// The renewal feature config the mobile client needs up front to gate the "renew" affordance before
/// any voucher is selected: whether the feature is on, the trigger window in days, and the tier ladder
/// with each tier's per-litre rate and whether the manager currently offers it. Prices are per-voucher
/// (they depend on litres), so this carries only the rate — the client gets concrete prices from the
/// quote endpoint.
/// </summary>
public sealed class RenewalConfigResponse
{
    public bool Enabled { get; set; }

    public int ThresholdDays { get; set; }

    public List<RenewalTierInfo> Tiers { get; set; } = new();

    public static RenewalConfigResponse From(VoucherRenewalConfig config)
        => new()
        {
            Enabled = config.Enabled,
            ThresholdDays = config.TriggerThresholdDays,
            Tiers = config.Tiers
                .Select(t => new RenewalTierInfo
                {
                    Term = t.Term.Code(),
                    RatePerLiterUah = t.RatePerLiterUah,
                    Offerable = t.IsOfferable
                })
                .ToList()
        };
}

/// <summary>One tier as the client sees it in config: its stable code, the per-litre rate, and whether it is offered.</summary>
public sealed class RenewalTierInfo
{
    public string Term { get; set; } = string.Empty;

    public decimal RatePerLiterUah { get; set; }

    /// <summary>True when the manager has enabled the tier AND set a positive rate (config-only; ignores stock).</summary>
    public bool Offerable { get; set; }
}
