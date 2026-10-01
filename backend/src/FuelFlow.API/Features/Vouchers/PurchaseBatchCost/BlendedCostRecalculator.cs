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

    public BlendedCostRecalculator(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Blended cost/liter across the whole in-stock pool for <paramref name="fuelTypeId"/>
    /// (which already encodes supplier+fuel, e.g. "okko-dp"), or <c>null</c> when no cost-bearing
    /// stock exists yet.
    /// </summary>
    /// <remarks>
    /// The pool = owned-but-unsold vouchers whose batch has a cost: statuses
    /// <see cref="VoucherStatus.Imported"/> / <see cref="VoucherStatus.VerifiedWithWarnings"/> /
    /// <see cref="VoucherStatus.Available"/>. It EXCLUDES Assigned/Used/Expired/Blocked/Deactivated/
    /// VerificationFailed. Imported counts on purpose — liters enter the pool at import, so the
    /// blended price steps on batch load, not on activation (activation is not a cost event).
    /// </remarks>
    public async Task<decimal?> ComputeBlendedAsync(string fuelTypeId, CancellationToken ct = default)
    {
        var pool = await (
            from v in _context.FuelVouchers.IgnoreQueryFilters()
            where v.FuelTypeId == fuelTypeId
                && v.ImportJobId != null
                && (v.Status == VoucherStatus.Imported
                    || v.Status == VoucherStatus.VerifiedWithWarnings
                    || v.Status == VoucherStatus.Available)
            join b in _context.PurchaseBatches
                on new { v.ImportJobId, v.FuelTypeId }
                equals new { ImportJobId = (Guid?)b.ImportJobId, b.FuelTypeId }
            select new { v.Liters, b.CostPerLiter })
            .ToListAsync(ct);

        return PurchaseBatchCosting.BlendedCostPerLiter(pool.Select(p => (p.Liters, p.CostPerLiter)));
    }

    /// <summary>
    /// Rewrites every fuel package for <paramref name="fuelTypeId"/> to price off
    /// <paramref name="blendedCost"/>, preserving each package's profit/pump/min-discount knobs, and
    /// syncs the fuel type's base/discount headline — mirroring <c>ProvidersController.UpdateFuel</c>.
    /// Does NOT call SaveChanges (the caller batches the save); returns the number of packages repriced.
    /// </summary>
    public async Task<int> RepriceAsync(string fuelTypeId, decimal blendedCost, Guid actingUserId, CancellationToken ct = default)
    {
        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == fuelTypeId).ToListAsync(ct);
        if (packages.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var actingUser = actingUserId == Guid.Empty ? (Guid?)null : actingUserId;
        decimal firstFinal = 0m, firstBase = 0m;

        for (var i = 0; i < packages.Count; i++)
        {
            var pkg = packages[i];
            var profit = pkg.MarginUahPerLiter ?? 0m;
            var pump = pkg.PumpPricePerLiter;
            var minDiscount = pkg.MinDiscountPerLiter ?? 0m;

            // Same shared formula the operator panel uses: final = min(cost + profit, pump − minDiscount);
            // pump null ⇒ cost + profit.
            var finalPerLiter = FuelPricing.FinalPerLiter(blendedCost, profit, pump, minDiscount);
            var price = Math.Round(finalPerLiter * pkg.Liters, 2, MidpointRounding.AwayFromZero);

            pkg.SupplierPricePerLiter = blendedCost;
            pkg.FinalPricePerLiter = finalPerLiter;
            pkg.Price = price;
            pkg.OriginalPrice = FuelPricing.OriginalPackagePrice(pump, pkg.Liters, price);
            pkg.UpdatedAtUtc = now;
            pkg.PriceUpdatedAt = now;
            pkg.PriceUpdatedByUserId = actingUser;

            if (i == 0)
            {
                firstFinal = finalPerLiter;
                firstBase = pump ?? (finalPerLiter + minDiscount);
            }
        }

        _context.FuelPackages.UpdateRange(packages);

        // Keep the fuel-type headline (base/discount UAH/L) in step, as UpdateFuel does.
        var fuelType = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelTypeId, ct);
        if (fuelType is not null)
        {
            fuelType.BasePrice = Math.Round(firstBase, 2, MidpointRounding.AwayFromZero);
            fuelType.DiscountPrice = Math.Round(firstFinal, 2, MidpointRounding.AwayFromZero);
            fuelType.UpdatedAtUtc = now;
            _context.FuelTypes.Update(fuelType);
        }

        return packages.Count;
    }
}
