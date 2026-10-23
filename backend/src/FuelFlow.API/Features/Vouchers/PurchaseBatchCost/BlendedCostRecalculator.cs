using FuelFlow.Features.Pricing;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.PurchaseBatchCost;

/// <summary>
/// Recomputes the blended moving-average cost for one supplier+fuel from live stock and writes it
/// into that fuel's packages so slice 1a's pricing engine (<see cref="FuelPricing"/> / ServerPricing)
/// prices off REAL invoice cost (pricing epic slice 2a, spec §1). Below-cost prices are surfaced,
/// not blocked here — the hard block is slice 3.
/// </summary>
public sealed class BlendedCostRecalculator
{
    private readonly ApplicationDbContext _context;
    private readonly PriceHistoryRecorder _priceRecorder;

    public BlendedCostRecalculator(ApplicationDbContext context, PriceHistoryRecorder priceRecorder)
    {
        _context = context;
        _priceRecorder = priceRecorder;
    }

    /// <summary>
    /// Blended cost/liter across the whole in-stock pool for <paramref name="fuelTypeId"/>
    /// (which already encodes supplier+fuel, e.g. "okko-dp"), or <c>null</c> when no cost-bearing
    /// stock exists yet.
    /// </summary>
    /// <remarks>
    /// The pool = owned-but-unsold vouchers that carry a cost: statuses
    /// <see cref="VoucherStatus.Imported"/> / <see cref="VoucherStatus.DecodeIntegrityWarning"/> /
    /// <see cref="VoucherStatus.Available"/>. It EXCLUDES Assigned/Used/Expired/Blocked/Deactivated/
    /// DecodeIntegrityFailed. Imported counts on purpose — liters enter the pool at import, so the
    /// blended price steps on batch load, not on activation (activation is not a cost event).
    /// Cost comes from the voucher itself, not from a batch join: vouchers of one batch are not
    /// interchangeable, because a customer payment on one of them reduces that voucher alone.
    /// Soft-deleted rows are excluded explicitly as well as by the model's global <c>!IsDeleted</c>
    /// filter: this number reprices every package, so a deleted voucher must not keep influencing
    /// what buyers are quoted (#216) even if a narrower scope is added to this query later.
    /// </remarks>
    public async Task<decimal?> ComputeBlendedAsync(Guid fuelTypeId, CancellationToken ct = default)
    {
        var pool = await (
            from v in _context.FuelVouchers
            where v.FuelTypeId == fuelTypeId
                && !v.IsDeleted
                && v.CostPerLiter != null
                && (v.Status == VoucherStatus.Imported
                    || v.Status == VoucherStatus.DecodeIntegrityWarning
                    || v.Status == VoucherStatus.Available)
            select new { v.Liters, CostPerLiter = v.CostPerLiter!.Value })
            .ToListAsync(ct);

        return PurchaseBatchCosting.BlendedCostPerLiter(pool.Select(p => (p.Liters, p.CostPerLiter)));
    }

    /// <summary>
    /// Rewrites the fuel's price to sit off <paramref name="blendedCost"/>, preserving its
    /// pump/min-discount knobs, syncing its base/discount headline — mirroring
    /// <c>BrandsController.UpdateFuel</c> — and appending the moved price to <c>fuel_price_history</c>.
    /// Does NOT call SaveChanges (the caller batches the save); returns the number of packages repriced.
    /// </summary>
    /// <param name="reason">
    /// Which door the cost came through, from <see cref="PriceHistoryReasons"/>: an operator entering a
    /// batch cost, or a voucher exchange releasing expired stock back into the pool. Both end here, and
    /// the history row is the only thing that says which.
    /// </param>
    public async Task<int> RepriceAsync(
        Guid fuelTypeId,
        decimal blendedCost,
        Guid actingUserId,
        string reason = PriceHistoryReasons.BatchCost,
        CancellationToken ct = default)
    {
        // The cost is now the fuel's, and so are its profit/pump/min-discount knobs: there is one price
        // per grade, so this method has a single row to write and no way for two pools of it to
        // drift apart.
        var fuel = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelTypeId, ct);
        if (fuel is null) return 0;

        var margin = fuel.MarginUahPerLiter ?? 0m;

// How many sizes this fuel is sold in: what the cost correction moved the price of. A nominal
    // carries no stored total, so there is nothing to rewrite here - every size follows its fuel's
    // rate automatically - but the count is what the caller reports back to the operator.
    var nominalCount = await _context.FuelNominals
    .AsNoTracking()
    .CountAsync(n => n.FuelTypeId == fuelTypeId, ct);

    var now = SystemClock.UtcNow;
        var actingUser = actingUserId == Guid.Empty ? (Guid?)null : actingUserId;

        // Same shared formula the operator panel uses: final = cost + profit. The column price is the
        // reference the customer is shown against, not a term of the price, so a new cost moves the
        // price and a pump move does not. Computed here to keep the fuel-type headline in step; the
        // stored figures stopped duplicating it on 2026-10-17 and derive it from the lines this writes.
        var finalPerLiter = FuelPricing.FinalPerLiter(blendedCost, margin);

        fuel.SupplierPricePerLiter = blendedCost;
        fuel.UpdatedAtUtc = now;

        _context.FuelTypes.Update(fuel);

        // The history, in one call: the recorder stamps
        // price_updated_at / price_updated_by_user_id (this method used to) and appends a row if the
        // cost or the final price actually moved. It does not save - the caller owns that.
        await _priceRecorder.RecordRangeAsync([fuel], reason, actingUser, now, ct);

        return nominalCount;
    }
}
