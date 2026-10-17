using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetFuelPriceHistory;

/// <summary>
/// What one fuel was priced at, oldest first, so an operator can read the sequence rather than a
/// single number.
/// </summary>
/// <param name="FuelTypeId">The fuel. A fuel has one offer per pool, and the response says which.</param>
/// <param name="Limit">
/// How many of the newest rows to read. The order in the response is oldest-first within that window,
/// because the last row is the current price and the first is where the window started - reversing it
/// would put the current price at the bottom of a chart.
/// </param>
public sealed record GetFuelPriceHistoryQuery(string FuelTypeId, int Limit = 50);

/// <summary>
/// One recorded price. Deliberately flat and numeric: the six per-litre facts plus when, who and why.
/// </summary>
/// <param name="DataKind">Which pool's offer this row belongs to, so a rehearsal row is never read as a live price.</param>
/// <param name="EffectiveFromUtc">
/// When this price took effect. The column a "what did it cost on Tuesday" question filters on.
/// </param>
/// <param name="Reason">
/// Which write path moved the price, from <see cref="OfferPriceReasons"/>. The audit trail says what
/// changed; this says whether a person, the pump import or a batch cost produced it.
/// </param>
public sealed record FuelPriceHistoryEntry(
    Guid Id,
    string OfferId,
    string DataKind,
    decimal? SupplierPricePerLiter,
    decimal? MarginUahPerLiter,
    decimal? MarginPercent,
    decimal? FinalPricePerLiter,
    decimal? PumpPricePerLiter,
    decimal? MinDiscountPerLiter,
    DateTime EffectiveFromUtc,
    Guid? ChangedByUserId,
    string Reason);

/// <summary>
/// Reads <c>fuel_offer_prices</c> for one fuel, newest window first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The table was written for one purpose the previous shape could not serve:
/// money already moved cannot be re-derived from the present. The dashboard, reconciliation and the
/// report all multiply today's margin by historical order lines, so a margin change today silently
/// restates yesterday's profit - and before this table there was nothing to restate it from. An
/// operator asking "what was this priced at last week" now gets an answer instead of the current number.
/// </para>
/// <para>
/// <b>What it deliberately does not do.</b> It reads the history only. The current price stays
/// <see cref="FuelOffer"/>'s own columns - the storefront, checkout and availability all read those -
/// so this endpoint is a window onto the record, not a second source of the price. Asking for both from
/// one place would invite a client to prefer the slower one.
/// </para>
/// <para>
/// <b>Both pools.</b> A fuel has an offer per pool and the two are priced independently, so a fuel's
/// history is interleaved. The rows carry <c>data_kind</c> for exactly that reason: a rehearsal price
/// read as a live one is the mistake this column exists to prevent everywhere else in the schema.
/// </para>
/// </remarks>
public sealed class GetFuelPriceHistoryQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetFuelPriceHistoryQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPriceHistoryEntry>> HandleAsync(
        GetFuelPriceHistoryQuery query,
        CancellationToken ct = default)
    {
        var newest = await _context.FuelOfferPrices
            .AsNoTracking()
            .Where(p => p.FuelTypeId == query.FuelTypeId)
            .OrderByDescending(p => p.EffectiveFromUtc)
            .ThenByDescending(p => p.Id)
            .Take(PageLimits.ClampPageSize(query.Limit))
            .Select(p => new FuelPriceHistoryEntry(
                p.Id,
                p.OfferId,
                p.DataKind,
                p.SupplierPricePerLiter,
                p.MarginUahPerLiter,
                p.MarginPercent,
                p.FinalPricePerLiter,
                p.PumpPricePerLiter,
                p.MinDiscountPerLiter,
                p.EffectiveFromUtc,
                p.ChangedByUserId,
                p.Reason))
            .ToListAsync(ct);

        // Read newest-first, return oldest-first: the list is a sequence, and a sequence whose last
        // element is the current price is one that can be charted without reversing it first.
        newest.Reverse();
        return newest;
    }
}