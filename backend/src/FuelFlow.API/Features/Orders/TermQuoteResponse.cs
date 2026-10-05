using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Terms;

namespace FuelFlow.API.Features.Orders;

/// <summary>
/// What the mobile term picker needs to render one cart line: the feature gate, and every term with the
/// price the customer would actually pay for that line.
/// </summary>
/// <remarks>
/// Read-only and non-binding. Nothing is reserved and no price is frozen here - checkout recomputes the
/// discount server-side from the catalog, so the figure shown can never become the figure charged by
/// trusting the client.
/// </remarks>
public sealed record TermQuoteResponse(
    bool Enabled,
    IReadOnlyList<TermQuoteItem> Terms)
{
    public static TermQuoteResponse From(VoucherTermConfig config, IReadOnlyList<TermQuoteItem> terms)
        => new(config.Enabled, terms);
}

/// <summary>One term the customer can buy this fuel for.</summary>
/// <param name="Term">Term code (<c>1w</c>…<c>6m</c>).</param>
/// <param name="DiscountPerLiterUah">UAH off the per-litre price for this term.</param>
/// <param name="PricePerLiterUah">The resulting per-litre price. Null when no package price could be resolved.</param>
/// <param name="LinePriceUah">Price for the whole line at <paramref name="Liters"/>.</param>
/// <param name="Liters">Nominal of the line this quote is for.</param>
/// <param name="Available">
/// Whether this term can actually be bought. False for a tier the manager left unpriced or switched off,
/// for a package that no longer exists, and for a tier whose discount would sell the fuel under cost —
/// the same refusal checkout applies. Advertising a term that payment then rejects would be worse than
/// not showing it, so the picker greys it out with the reason instead.
/// </param>
public sealed record TermQuoteItem(
    string Term,
    decimal DiscountPerLiterUah,
    decimal? PricePerLiterUah,
    decimal LinePriceUah,
    decimal Liters,
    bool Available);