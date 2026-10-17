using FuelFlow.Features.Pricing;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
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
    private readonly OfferPriceRecorder _priceRecorder;

    public BlendedCostRecalculator(ApplicationDbContext context, OfferPriceRecorder priceRecorder)
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
    /// <see cref="VoucherStatus.Imported"/> / <see cref="VoucherStatus.VerifiedWithWarnings"/> /
    /// <see cref="VoucherStatus.Available"/>. It EXCLUDES Assigned/Used/Expired/Blocked/Deactivated/
    /// VerificationFailed. Imported counts on purpose — liters enter the pool at import, so the
    /// blended price steps on batch load, not on activation (activation is not a cost event).
    /// Cost comes from the voucher itself, not from a batch join: vouchers of one batch are not
    /// interchangeable, because a customer payment on one of them reduces that voucher alone.
    /// Soft-deleted rows are excluded explicitly: <c>IgnoreQueryFilters</c> lifts the model's global
    /// <c>!IsDeleted</c> filter, and this number reprices every package, so a deleted voucher must not
    /// keep influencing what buyers are quoted (#216).
    /// </remarks>
    public async Task<decimal?> ComputeBlendedAsync(string fuelTypeId, CancellationToken ct = default)
    {
        var pool = await (
            from v in _context.FuelVouchers.IgnoreQueryFilters()
            where v.FuelTypeId == fuelTypeId
                && !v.IsDeleted
                && v.CostPerLiter != null
                && (v.Status == VoucherStatus.Imported
                    || v.Status == VoucherStatus.VerifiedWithWarnings
                    || v.Status == VoucherStatus.Available)
            select new { v.Liters, CostPerLiter = v.CostPerLiter!.Value })
            .ToListAsync(ct);

        return PurchaseBatchCosting.BlendedCostPerLiter(pool.Select(p => (p.Liters, p.CostPerLiter)));
    }

    /// <summary>
    /// Rewrites every offer for <paramref name="fuelTypeId"/> to price off <paramref name="blendedCost"/>,
    /// preserving each offer's profit/pump/min-discount knobs, restating each volume's frozen package
    /// total, syncing the fuel type's base/discount headline — mirroring
    /// <c>BrandsController.UpdateFuel</c> — and appending each moved price to <c>fuel_offer_prices</c>.
    /// Does NOT call SaveChanges (the caller batches the save); returns the number of packages repriced.
    /// </summary>
    /// <param name="reason">
    /// Which door the cost came through, from <see cref="OfferPriceReasons"/>: an operator entering a
    /// batch cost, or a voucher exchange releasing expired stock back into the pool. Both end here, and
    /// the history row is the only thing that says which.
    /// </param>
    public async Task<int> RepriceAsync(
        string fuelTypeId,
        decimal blendedCost,
        Guid actingUserId,
        string reason = OfferPriceReasons.BatchCost,
        CancellationToken ct = default)
    {
        var offers = await _context.FuelOffers.Where(o => o.FuelTypeId == fuelTypeId).ToListAsync(ct);
        if (offers.Count == 0) return 0;

        // The volumes are loaded only to restate each package's frozen total from the new per-litre
        // price. The cost and the margin belong to the offer, once.
        var offerIds = offers.Select(o => o.Id).ToList();
        var volumes = await _context.FuelOfferVolumes
            .Where(v => offerIds.Contains(v.OfferId) && !v.IsDeleted)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var actingUser = actingUserId == Guid.Empty ? (Guid?)null : actingUserId;
        decimal firstFinal = 0m, firstBase = 0m;

        foreach (var offer in offers)
        {
            var profit = offer.MarginUahPerLiter ?? 0m;
            var pump = offer.PumpPricePerLiter;
            var minDiscount = offer.MinDiscountPerLiter ?? 0m;

            // Same shared formula the operator panel uses: final = min(cost + profit, pump − minDiscount);
            // pump null ⇒ cost + profit.
            var finalPerLiter = FuelPricing.FinalPerLiter(blendedCost, profit, pump, minDiscount);

            offer.SupplierPricePerLiter = blendedCost;
            offer.FinalPricePerLiter = finalPerLiter;
            offer.UpdatedAtUtc = now;

            foreach (var volume in volumes.Where(v => v.OfferId == offer.Id))
            {
                volume.PackagePrice = Math.Round(finalPerLiter * volume.Liters, 2, MidpointRounding.AwayFromZero);
                volume.UpdatedAtUtc = now;
            }

            firstFinal = finalPerLiter;
            firstBase = pump ?? (finalPerLiter + minDiscount);
        }

        _context.FuelOffers.UpdateRange(offers);
        if (volumes.Count > 0) _context.FuelOfferVolumes.UpdateRange(volumes);

        // The history, in one call for every pool's offer: the recorder stamps
        // price_updated_at / price_updated_by_user_id (this method used to) and appends a row per offer
        // whose cost or final price actually moved. It does not save - the caller owns that.
        await _priceRecorder.RecordRangeAsync(offers, reason, actingUser, now, ct);

        // Keep the fuel-type headline (base/discount UAH/L) in step, as UpdateFuel does.
        var fuelType = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelTypeId, ct);
        if (fuelType is not null)
        {
            fuelType.BasePrice = Math.Round(firstBase, 2, MidpointRounding.AwayFromZero);
            fuelType.DiscountPrice = Math.Round(firstFinal, 2, MidpointRounding.AwayFromZero);
            fuelType.UpdatedAtUtc = now;
            _context.FuelTypes.Update(fuelType);
        }

        return volumes.Count;
    }
}
