using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing;

/// <summary>
/// The one place a fuel offer's price is allowed to change, and the place that leaves the evidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a choke point rather than a convention.</b> Five write paths move a price: the operator's
/// add-fuel and update-fuel endpoints, the OKKO catalogue import, the OKKO pump-price import, and the
/// blended-cost recalculator behind an operator-entered batch cost (which a voucher exchange also
/// calls). Two of the five did not stamp <c>price_updated_at</c> or <c>price_updated_by_user_id</c>
/// at all, and every one of them overwrote the price in place, so nothing said what the price had
/// been before. A convention - "remember to append a row" - is a convention the sixth path forgets,
/// and the failure is invisible: the price is right today and the history is a hole.
/// </para>
///
/// <para>
/// <b>What it does, in order.</b> It stamps the offer's two "last repriced" columns (which is why
/// they can no longer be missed), then appends a <see cref="FuelOfferPrice"/> row holding the six
/// per-litre facts as they now stand. It does not save: the callers already own the transaction
/// boundary, and a service that saved on its own would split a price change from the volume totals
/// restated alongside it.
/// </para>
///
/// <para>
/// <b>A price that did not move records no history row.</b> The pump-price import runs unattended every
/// quarter hour over the whole catalogue; appending a row per fuel per run would bury the changes in
/// noise and make "what changed on Tuesday" unreadable. So the recorder compares against the newest
/// row the offer already has, and returns <c>false</c> when the six facts are identical. It does not
/// compare timestamps - a reprice to the same number is not a price change.
/// </para>
///
/// <para>
/// The offer's own <c>price_updated_at</c> is stamped either way, because that is what three of the
/// five paths already did unconditionally and what the column has always meant: when this offer was
/// last repriced, whether or not the number came out different. Only the history row is reserved for
/// actual changes.
/// </para>
/// </remarks>
public sealed class OfferPriceRecorder
{
    private readonly ApplicationDbContext _context;

    public OfferPriceRecorder(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Stamps <paramref name="offer"/> and appends its price to the history, unless the price is
    /// unchanged. Returns whether a row was appended.
    /// </summary>
    /// <param name="offer">Tracked, already mutated to its new price.</param>
    /// <param name="reason">From <see cref="OfferPriceReasons"/>; the database refuses an unknown one.</param>
    /// <param name="actingUserId">Who moved it, or null for an unattended import.</param>
    /// <param name="effectiveFromUtc">
    /// When the price took effect. Defaults to now. A caller that already computed one timestamp for a
    /// whole batch of offers passes it, so a single import carries a single effective time.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    public async Task<bool> RecordAsync(
        FuelOffer offer,
        string reason,
        Guid? actingUserId,
        DateTime? effectiveFromUtc = null,
        CancellationToken ct = default)
    {
        var latest = await LatestForAsync([offer.Id], ct);
        var fuels = await FuelsForAsync([offer.FuelTypeId], ct);
        return Record(offer, reason, actingUserId, effectiveFromUtc, latest, fuels);
    }

    /// <summary>
    /// The range form, for the write paths that reprice every offer of a fuel or the whole catalogue at
    /// once. The newest existing rows are fetched in one query rather than one per offer, which is the
    /// difference between one round trip and twenty-five for a pump-price import.
    /// </summary>
    /// <param name="offers">Tracked, already mutated to their new prices.</param>
    /// <param name="reason">From <see cref="OfferPriceReasons"/>.</param>
    /// <param name="actingUserId">Who moved them, or null for an unattended import.</param>
    /// <param name="effectiveFromUtc">Defaults to now; pass the batch's own timestamp when there is one.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How many rows were appended.</returns>
    public async Task<int> RecordRangeAsync(
        IEnumerable<FuelOffer> offers,
        string reason,
        Guid? actingUserId,
        DateTime? effectiveFromUtc = null,
        CancellationToken ct = default)
    {
        var tracked = offers.ToList();
        if (tracked.Count == 0) return 0;

        var latest = await LatestForAsync(tracked.Select(o => o.Id).ToList(), ct);
        var fuels = await FuelsForAsync(tracked.Select(o => o.FuelTypeId).Distinct().ToList(), ct);
        return tracked.Count(o => Record(o, reason, actingUserId, effectiveFromUtc, latest, fuels));
    }

    /// <summary>
    /// The newest history row per offer, as <c>(offer id, row)</c>. Ordered by
    /// <c>effective_from</c> and then by id, which is the same "current" rule
    /// <see cref="FuelOfferPrice"/> states - the id is a version 7 UUID, so it breaks a tie
    /// chronologically.
    /// </summary>
    private async Task<Dictionary<string, FuelOfferPrice>> LatestForAsync(
        List<string> offerIds,
        CancellationToken ct)
    {
        var rows = await _context.FuelOfferPrices
            .AsNoTracking()
            .Where(p => offerIds.Contains(p.OfferId))
            .OrderBy(p => p.EffectiveFromUtc)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        // Ascending order plus overwrite-on-equal keeps the last row written for each offer.
        var latest = new Dictionary<string, FuelOfferPrice>(StringComparer.Ordinal);
        foreach (var row in rows) latest[row.OfferId] = row;
        return latest;
    }

    /// <summary>
    /// The fuels whose margins the rows below will record, as <c>fuel id, fuel</c>.
    /// <para>
    /// Fetched here for the same reason <see cref="LatestForAsync"/> fetches there: the recorder is the
    /// one place a price becomes history, so it is the one place that has to know where every number in
    /// that row comes from. Reading the margin off the offer would have meant a fifth writer to keep in
    /// step; asking each caller for it would have made it a parameter on two methods and a chance to
    /// pass the wrong fuel's number. One query, one place, no caller can get it wrong.
    /// </para>
    /// <para>
    /// <b>The tracker is consulted before the database, and that ordering is load-bearing.</b> Callers
    /// mutate the fuel in memory and call this before saving - the recorder does not save, because they
    /// own the transaction boundary. A plain query would read the committed margin and record the price
    /// that was just superseded, so the history's newest row would be the wrong one. Reading a tracked
    /// entity returns the pending value; only a fuel nobody has touched in this context needs a query.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, FuelTypeEntity>> FuelsForAsync(
        List<string> fuelTypeIds,
        CancellationToken ct)
    {
        var pending = context_FuelTypes_LocalOrEmpty(fuelTypeIds);
        var missing = fuelTypeIds.Where(id => !pending.ContainsKey(id)).ToList();

        if (missing.Count > 0)
        {
            var rows = await _context.FuelTypes
                .AsNoTracking()
                .Where(f => missing.Contains(f.Id))
                .ToListAsync(ct);
            foreach (var row in rows) pending[row.Id] = row;
        }

        return pending;
    }

    /// <summary>
    /// The fuels among <paramref name="ids"/> this context is already holding, under their own key so the
    /// result can be filled in with the rest. Exposed as a method only to keep the null-vs-empty
    /// distinction out of the query above.
    /// </summary>
    private Dictionary<string, FuelTypeEntity> context_FuelTypes_LocalOrEmpty(List<string> ids)
    {
        var pending = new Dictionary<string, FuelTypeEntity>(StringComparer.Ordinal);
        foreach (var entry in _context.ChangeTracker.Entries<FuelTypeEntity>())
        {
            if (entry.State == EntityState.Detached) continue;
            if (ids.Contains(entry.Entity.Id)) pending[entry.Entity.Id] = entry.Entity;
        }
        return pending;
    }

    /// <summary>The margin an offer sells at, which is its fuel's. Null when the fuel is not to hand.</summary>
    private static decimal? MarginOf(FuelOffer offer, Dictionary<string, FuelTypeEntity> fuels)
        => fuels.TryGetValue(offer.FuelTypeId, out var fuel) ? fuel.MarginUahPerLiter : null;

    /// <summary>
    /// The synchronous half, so both public methods decide identically: stamp, compare, append.
    /// </summary>
    /// <returns>Whether a row was appended.</returns>
    private bool Record(
        FuelOffer offer,
        string reason,
        Guid? actingUserId,
        DateTime? effectiveFromUtc,
        Dictionary<string, FuelOfferPrice> latest,
        Dictionary<string, FuelTypeEntity> fuels)
    {
        var at = effectiveFromUtc ?? DateTime.UtcNow;
        var margin = MarginOf(offer, fuels);

        offer.PriceUpdatedAt = at;
        offer.PriceUpdatedByUserId = actingUserId == Guid.Empty ? null : actingUserId;

        if (latest.TryGetValue(offer.Id, out var previous) && SamePrice(previous, offer, margin)) return false;

        _context.FuelOfferPrices.Add(new FuelOfferPrice
        {
            Id = Guid.CreateVersion7(),
            OfferId = offer.Id,
            BrandId = offer.BrandId,
            FuelTypeId = offer.FuelTypeId,
            DataKind = offer.DataKind,
            SupplierPricePerLiter = offer.SupplierPricePerLiter,
            MarginUahPerLiter = margin,
            MarginPercent = offer.MarginPercent,
            // The derived figure, recorded rather than read: the offer stopped storing it on
            // 2026-10-17, and a history row that left it null would answer "what did this cost?" with
            // the knobs instead of the number. Derived from the fuel's margin, not the offer's copy of
            // it, for the same reason the row above is.
            FinalPricePerLiter = FuelPricing.FinalPerLiterOrNull(
                offer.SupplierPricePerLiter, margin, offer.PumpPricePerLiter, offer.MinDiscountPerLiter),
            PumpPricePerLiter = offer.PumpPricePerLiter,
            MinDiscountPerLiter = offer.MinDiscountPerLiter,
            EffectiveFromUtc = at,
            RecordedAtUtc = DateTime.UtcNow,
            ChangedByUserId = actingUserId == Guid.Empty ? null : actingUserId,
            Reason = reason
        });

        return true;
    }

    /// <summary>
    /// Whether the six per-litre facts are the same. Decimal equality, not string equality: the column
    /// is <c>numeric(10,4)</c> and EF hands the entity back whatever precision the round trip kept, so
    /// <c>51.00</c> and <c>51.0000</c> are the same price and must not open a new row.
    /// <para>
    /// The comparison includes the derived figure, which is redundant while the knobs match — two
    /// identical knob sets always derive the same price. It is compared anyway so the row this writes
    /// and the row it compares against are the same six facts, and so a row written by an older build
    /// (which recorded the stored column) still compares correctly against a derived one.
    /// </para>
    /// </summary>
    private static bool SamePrice(FuelOfferPrice previous, FuelOffer offer, decimal? margin) =>
        previous.SupplierPricePerLiter == offer.SupplierPricePerLiter
        && previous.MarginUahPerLiter == margin
        && previous.MarginPercent == offer.MarginPercent
        && previous.FinalPricePerLiter == FuelPricing.FinalPerLiterOrNull(
            offer.SupplierPricePerLiter, margin, offer.PumpPricePerLiter, offer.MinDiscountPerLiter)
        && previous.PumpPricePerLiter == offer.PumpPricePerLiter
        && previous.MinDiscountPerLiter == offer.MinDiscountPerLiter;
}