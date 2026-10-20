using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing;

/// <summary>
/// The one place a fuel's price is allowed to change, and the place that leaves the evidence.
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
/// <b>What it does, in order.</b> It stamps the fuel's two "last repriced" columns (which is why
/// they can no longer be missed), then appends a <see cref="FuelPriceHistory"/> row holding the six
/// per-litre facts as they now stand. It does not save: the callers already own the transaction
/// boundary, and a service that saved on its own would split a price change from the volume totals
/// restated alongside it.
/// </para>
///
/// <para>
/// <b>A price that did not move records no history row.</b> The pump-price import runs unattended every
/// quarter hour over the whole catalogue; appending a row per fuel per run would bury the changes in
/// noise and make "what changed on Tuesday" unreadable. So the recorder compares against the newest
/// row the fuel already has, and returns <c>false</c> when the six facts are identical. It does not
/// compare timestamps - a reprice to the same number is not a price change.
/// </para>
///
/// <para>
/// The fuel's own <c>price_updated_at</c> is stamped either way, because that is what three of the
/// five paths already did unconditionally and what the column has always meant: when this fuel was
/// last repriced, whether or not the number came out different. Only the history row is reserved for
/// actual changes.
/// </para>
/// </remarks>
public sealed class PriceHistoryRecorder
{
    private readonly ApplicationDbContext _context;

    public PriceHistoryRecorder(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Stamps <paramref name="fuel"/> and appends its price to the history, unless the price is
    /// unchanged. Returns whether a row was appended.
    /// </summary>
    /// <param name="fuel">Tracked, already mutated to its new price.</param>
    /// <param name="reason">From <see cref="PriceHistoryReasons"/>; the database refuses an unknown one.</param>
    /// <param name="actingUserId">Who moved it, or null for an unattended import.</param>
    /// <param name="effectiveFromUtc">
    /// When the price took effect. Defaults to now. A caller that already computed one timestamp for a
    /// whole batch of fuels passes it, so a single import carries a single effective time.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    public async Task<bool> RecordAsync(
        FuelTypeEntity fuel,
        string reason,
        Guid? actingUserId,
        DateTime? effectiveFromUtc = null,
        CancellationToken ct = default)
    {
        var latest = await LatestForAsync([fuel.Id], ct);
        return Record(fuel, reason, actingUserId, effectiveFromUtc, latest);
    }

    /// <summary>
    /// The range form, for the write paths that reprice a whole batch of fuels or the whole catalogue at
    /// once. The newest existing rows are fetched in one query rather than one per fuel, which is the
    /// difference between one round trip and twenty-five for a pump-price import.
    /// </summary>
    /// <param name="fuels">Tracked, already mutated to their new prices.</param>
    /// <param name="reason">From <see cref="PriceHistoryReasons"/>.</param>
    /// <param name="actingUserId">Who moved them, or null for an unattended import.</param>
    /// <param name="effectiveFromUtc">Defaults to now; pass the batch's own timestamp when there is one.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>How many rows were appended.</returns>
    public async Task<int> RecordRangeAsync(
        IEnumerable<FuelTypeEntity> fuels,
        string reason,
        Guid? actingUserId,
        DateTime? effectiveFromUtc = null,
        CancellationToken ct = default)
    {
        var tracked = fuels.ToList();
        if (tracked.Count == 0) return 0;

        var latest = await LatestForAsync(tracked.Select(f => f.Id).Distinct().ToList(), ct);
        return tracked.Count(f => Record(f, reason, actingUserId, effectiveFromUtc, latest));
    }

    /// <summary>
    /// The newest history row per fuel, as <c>(fuel id, row)</c>. Ordered by
    /// <c>effective_from</c> and then by id, which is the same "current" rule
    /// <see cref="FuelPriceHistory"/> states - the id is a version 7 UUID, so it breaks a tie
    /// chronologically.
    /// </summary>
    private async Task<Dictionary<Guid, FuelPriceHistory>> LatestForAsync(
        List<Guid> fuelTypeIds,
        CancellationToken ct)
    {
        var rows = await _context.FuelPriceHistory
            .AsNoTracking()
            .Where(p => fuelTypeIds.Contains(p.FuelTypeId))
            .OrderBy(p => p.EffectiveFromUtc)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        // Ascending order plus overwrite-on-equal keeps the last row written for each fuel.
        var latest = new Dictionary<Guid, FuelPriceHistory>();
        foreach (var row in rows) latest[row.FuelTypeId] = row;
        return latest;
    }

    /// <summary>
    /// The synchronous half, so both public methods decide identically: stamp, compare, append.
    /// </summary>
    /// <returns>Whether a row was appended.</returns>
    private bool Record(
        FuelTypeEntity fuel,
        string reason,
        Guid? actingUserId,
        DateTime? effectiveFromUtc,
        Dictionary<Guid, FuelPriceHistory> latest)
    {
        var at = effectiveFromUtc ?? DateTime.UtcNow;
        var margin = fuel.MarginUahPerLiter;

        fuel.PriceUpdatedAt = at;
        fuel.PriceUpdatedByUserId = actingUserId == Guid.Empty ? null : actingUserId;

        if (latest.TryGetValue(fuel.Id, out var previous) && SamePrice(previous, fuel)) return false;

        _context.FuelPriceHistory.Add(new FuelPriceHistory
        {
            Id = Guid.CreateVersion7(),

            BrandId = fuel.BrandId,
            FuelTypeId = fuel.Id,
            DataKind = DataKinds.Real,
            SupplierPricePerLiter = fuel.SupplierPricePerLiter,
            MarginUahPerLiter = margin,
            MarginPercent = fuel.MarginPercent,
            // The derived figure, recorded rather than read: the catalogue stopped storing it on
            // 2026-10-17, and a history row that left it null would answer "what did this cost?" with
            // the knobs instead of the number. Derived from the fuel's own margin, for the same reason
            // the row above is.
            FinalPricePerLiter = FuelPricing.FinalPerLiterOrNull(
        fuel.SupplierPricePerLiter, margin, fuel.PumpPricePerLiter, fuel.MinDiscountPerLiter),
            PumpPricePerLiter = fuel.PumpPricePerLiter,
            MinDiscountPerLiter = fuel.MinDiscountPerLiter,
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
    private static bool SamePrice(FuelPriceHistory previous, FuelTypeEntity fuel) =>
        previous.SupplierPricePerLiter == fuel.SupplierPricePerLiter
        && previous.MarginUahPerLiter == fuel.MarginUahPerLiter
        && previous.MarginPercent == fuel.MarginPercent
        && previous.FinalPricePerLiter == FuelPricing.FinalPerLiterOrNull(
        fuel.SupplierPricePerLiter, fuel.MarginUahPerLiter, fuel.PumpPricePerLiter, fuel.MinDiscountPerLiter)
        && previous.PumpPricePerLiter == fuel.PumpPricePerLiter
        && previous.MinDiscountPerLiter == fuel.MinDiscountPerLiter;
}
