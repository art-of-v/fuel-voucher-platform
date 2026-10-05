using FuelFlow.Features.Vouchers.Renewal;

namespace FuelFlow.Features.Vouchers.Terms;

/// <summary>
/// Manager-configured state of one purchase term.
/// </summary>
/// <param name="Term">How much validity the customer buys.</param>
/// <param name="Enabled">Whether the term is offered at all.</param>
/// <param name="DiscountPerLiterUah">
/// Taken off the package price, in UAH per litre. A discount rather than an absolute price so the sell
/// price keeps coming from the cost + margin engine and stays capped by the pump price — a term can
/// never be configured into selling below cost, and the below-cost guard still applies on top.
/// </param>
public sealed record VoucherTermTierConfig(VoucherRenewalTerm Term, bool Enabled, decimal DiscountPerLiterUah)
{
    /// <summary>
    /// Sellable only when the manager both enabled it and set a positive discount. Fail-safe: no
    /// discount means not offerable, so a misconfigured tier is never silently free.
    /// </summary>
    public bool IsOfferable => Enabled && DiscountPerLiterUah > 0m;
}

/// <summary>
/// The whole term-sale configuration, read from <c>app_settings</c>. Missing rows resolve to fail-safe
/// defaults: feature off, every tier off with a zero discount — which is exactly today's behaviour.
/// </summary>
public sealed record VoucherTermConfig(
    bool Enabled,
    IReadOnlyList<VoucherTermTierConfig> Tiers)
{
    public VoucherTermTierConfig? Tier(VoucherRenewalTerm term)
        => Tiers.FirstOrDefault(t => t.Term == term);
}