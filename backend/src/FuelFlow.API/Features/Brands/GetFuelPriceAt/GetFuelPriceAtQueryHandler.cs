using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetFuelPriceAt;

/// <summary>
/// The price of one fuel at one instant in the past.
/// </summary>
/// <param name="FuelTypeId">The fuel to look up.</param>
/// <param name="AsOfUtc">
/// The instant to answer for. Not "the last change before now" — a specific moment, so an order line
/// from a month ago can be priced from the record rather than from today's margin.
/// </param>
public sealed record GetFuelPriceAtQuery(string FuelTypeId, DateTime AsOfUtc);

/// <summary>
/// The row that was in effect at <c>asOf</c>, with the moment it took effect and how long it stood.
/// </summary>
/// <param name="EffectiveFromUtc">When this price took effect.</param>
/// <param name="SupersededAtUtc">
/// When the next price took over, or null when this is the price in effect now. Derived, not stored:
/// it is the next row's <c>effective_from</c>, and storing it would be a second copy of a fact the
/// next row already owns.
/// </param>
public sealed record FuelPriceAt(
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
    DateTime? SupersededAtUtc,
    Guid? ChangedByUserId,
    string Reason);

/// <summary>
/// Answers "what was this fuel priced at, then", from <c>fuel_offer_prices</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a filter on the existing history endpoint.</b> That endpoint returns a series and
/// clamps it to <see cref="PageLimits.MaxPageSize"/> rows, which is right for charting and useless for
/// the question an order line asks: given a moment, which row was standing? Filtering a 200-row window
/// by date answers it only while the moment is inside that window, and a fuel repriced three times in
/// April is not reachable from it at all.
/// </para>
///
/// <para>
/// <b>Which row "the price at T" means.</b> The greatest <c>effective_from</c> at or before T, and
/// where two rows share a timestamp - two reprices inside one transaction - the greater
/// <see cref="FuelOfferPrice.Id"/>. That is the same rule
/// <see cref="GetFuelPriceHistoryQueryHandler"/> and <see cref="FuelOfferPrice"/> state for "current",
/// reused rather than reinvented: two definitions of "the price in effect" would disagree exactly when
/// it matters, which is on a row someone is about to settle money against.
/// </para>
///
/// <para>
/// <b>Null is an answer, not a failure.</b> A fuel created last week has no price in effect last month,
/// and "no price stood then" is what an operator needs to hear - it is the difference between a
/// question answered wrongly and a question that had no answer. A fuel with no rows at all returns null
/// too; the caller can tell the two apart with the history endpoint, and this one stays a single query.
/// </para>
///
/// <para>
/// <b>Backdated rows count.</b> <c>effective_from</c> is the column, not <c>recorded_at</c>: a price
/// entered this morning for last Tuesday is the price that stood last Tuesday, and reading by write
/// time would hide it.
/// </para>
///
/// <para>
/// <b>Both pools.</b> A fuel has one offer per pool and they are priced independently, so the two
/// series interleave. This returns the row with the greatest <c>effective_from</c> across both, and says
/// which pool it came from in <c>data_kind</c> - the same care the history endpoint takes, because a
/// rehearsal price read as a live one is the mistake that column exists to prevent everywhere else in
/// this schema.
/// </para>
/// </remarks>
public sealed class GetFuelPriceAtQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetFuelPriceAtQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// The price standing at <c>query.AsOfUtc</c>, or null when no row had taken effect by then.
    /// </summary>
    public async Task<FuelPriceAt?> HandleAsync(GetFuelPriceAtQuery query, CancellationToken ct = default)
    {
        var row = await _context.FuelOfferPrices
            .AsNoTracking()
            .Where(p => p.FuelTypeId == query.FuelTypeId && p.EffectiveFromUtc <= query.AsOfUtc)
            .OrderByDescending(p => p.EffectiveFromUtc)
            .ThenByDescending(p => p.Id)
            .Select(p => new
            {
                Price = new FuelPriceAt(
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
                    null, // superseded_at - filled below, from the next row
                    p.ChangedByUserId,
                    p.Reason),
                PriceId = p.Id,
                PriceFrom = p.EffectiveFromUtc,
                p.OfferId
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        // The row that displaced this one, so the caller can see the window it was the price for
        // rather than having to ask again to find out. Bounded to this fuel and strictly after this
        // row's instant, which is one indexed lookup on a table that is indexed on (offer, from).
        var supersededAt = await _context.FuelOfferPrices
            .AsNoTracking()
            .Where(p => p.FuelTypeId == query.FuelTypeId && p.EffectiveFromUtc > row.PriceFrom)
            .OrderBy(p => p.EffectiveFromUtc)
            .ThenBy(p => p.Id)
            .Select(p => (DateTime?)p.EffectiveFromUtc)
            .FirstOrDefaultAsync(ct);

        return row.Price with { SupersededAtUtc = supersededAt };
    }
}
