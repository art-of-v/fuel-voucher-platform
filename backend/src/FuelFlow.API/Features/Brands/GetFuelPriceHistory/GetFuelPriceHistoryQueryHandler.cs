using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetFuelPriceHistory;

/// <summary>
/// What one fuel was priced at, oldest first, so an operator can read the sequence rather than a
/// single number.
/// </summary>
/// <param name="FuelTypeId">The fuel. One price per grade, and the response says which row it came from.</param>
/// <param name="Limit">
/// How many of the newest rows to read. The order in the response is oldest-first within that window,
/// because the last row is the current price and the first is where the window started - reversing it
/// would put the current price at the bottom of a chart.
/// </param>
public sealed record GetFuelPriceHistoryQuery(Guid FuelTypeId, int Limit = 50);

/// <summary>
/// One recorded price. Deliberately flat and numeric: the six per-litre facts plus when, who and why.
/// </summary>
/// <param name="DataKind">Which side of the shelf the write came from, so a rehearsal row is never read as a live price.</param>
/// <param name="EffectiveFromUtc">
/// When this price took effect. The column a "what did it cost on Tuesday" question filters on.
/// </param>
/// <param name="Reason">
/// Which write path moved the price, from <see cref="PriceHistoryReasons"/>. The audit trail says what
/// changed; this says whether a person, the pump import or a batch cost produced it.
/// </param>
public sealed record FuelPriceHistoryEntry(
    Guid Id,
    Guid FuelTypeId,
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
/// Reads <c>fuel_price_history</c> for one fuel, newest window first.
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
/// <see cref="FuelTypeEntity"/>'s own columns - the storefront, checkout and availability all read those -
/// so this endpoint is a window onto the record, not a second source of the price. Asking for both from
/// one place would invite a client to prefer the slower one.
/// </para>
/// <para>
/// <b>Which side of the shelf a row came from.</b> One price per grade, so a fuel's rows are not
/// interleaved with another fuel's - but they do carry <c>data_kind</c>, and it says which side of the
/// shelf the write was made on. A rehearsal price read as a live one is the mistake this column exists
/// to prevent everywhere else in the schema.
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
        var newest = await _context.FuelPriceHistory
            .AsNoTracking()
            .Where(p => p.FuelTypeId == query.FuelTypeId)
            .OrderByDescending(p => p.EffectiveFromUtc)
            .ThenByDescending(p => p.Id)
            .Take(PageLimits.ClampPageSize(query.Limit))
            .Select(p => new FuelPriceHistoryEntry(
                p.Id,
                p.FuelTypeId,
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
