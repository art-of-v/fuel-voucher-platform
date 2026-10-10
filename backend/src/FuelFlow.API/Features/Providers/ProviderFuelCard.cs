using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Providers;

/// <summary>
/// Builds a fuel's pricing card from its packages. Shared by <c>/providers</c> and
/// <c>/providers/{id}</c> so the two can never describe the same fuel differently.
/// </summary>
/// <remarks>
/// A card has one row per fuel but a fuel has one package per nominal, and the write path
/// (<c>ProvidersController.UpdateFuel</c>) writes the same six figures to <em>every</em> package of
/// the fuel. So the card's job is to report one canonical row — and, when the packages disagree, to
/// say so, because a save from this screen will silently flatten them to whatever is shown.
///
/// The previous behaviour was <c>packages.FirstOrDefault()</c> over an unordered result: six
/// pricing fields read off whichever row the database happened to return. With four packages for
/// one fuel that meant the card could show a price that exists nowhere the customer can buy — and
/// the number could change between two identical page loads.
/// </remarks>
internal static class ProviderFuelCard
{
    /// <summary>
    /// Cost per litre, weighted by the litres each package actually sells. This is the figure the
    /// below-cost decision is about, and it matches how the pool's blended cost is computed, so the
    /// card describes what will be enforced rather than one nominal's share of it.
    /// </summary>
    public static decimal WeightedCostPerLiter(IReadOnlyList<FuelPackage> packages)
    {
        if (packages.Count == 0) return 0m;
        var weighted = packages.Where(p => p.SupplierPricePerLiter is not null).ToList();
        if (weighted.Count == 0) return 0m;

var litres = weighted.Sum(p => p.Liters);
        if (litres <= 0m) return weighted.Average(p => p.SupplierPricePerLiter!.Value);

        var weightedTotal = weighted.Sum(p => p.SupplierPricePerLiter!.Value * p.Liters);
        // 4 decimals, AwayFromZero: the same scale and rounding as the stored cost_per_liter, so a
        // displayed average and a stored one cannot disagree by a rounding step.
        return Math.Round(weightedTotal / litres, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The nominal the remaining per-package figures are read from: the smallest one, ties broken by
    /// id. Deterministic, and the nominal an operator reads the card as ("what does a litre of this
    /// cost") is the smallest one. When packages disagree, <see cref="ProviderFuelDto.PriceSpread"/>
    /// is what makes that visible.
    /// </summary>
    private static FuelPackage? Reference(IReadOnlyList<FuelPackage> packages) =>
        packages
            .OrderBy(p => p.Liters)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    public static ProviderFuelDto Build(FuelTypeEntity fuel, IReadOnlyList<FuelPackage> packages)
    {
        var reference = Reference(packages);

        var supplierCosts = packages
            .Where(p => p.SupplierPricePerLiter is not null)
            .Select(p => p.SupplierPricePerLiter!.Value)
            .ToList();
        var finals = packages
            .Where(p => p.FinalPricePerLiter is not null)
            .Select(p => p.FinalPricePerLiter!.Value)
            .ToList();

        return new ProviderFuelDto
        {
            Id = fuel.Id,
            Name = fuel.Name,
            SupplierPricePerLiter = WeightedCostPerLiter(packages),
            MarginUahPerLiter = reference?.MarginUahPerLiter ?? 0,
            MarginPercent = reference?.MarginPercent,
            FinalPricePerLiter = reference?.FinalPricePerLiter ?? 0,
            PumpPricePerLiter = reference?.PumpPricePerLiter,
            // Fall back to the historical marketing discount (base - final) so a fuel
            // priced before the min-discount column existed shows a sane initial knob.
            MinDiscountPerLiter = reference?.MinDiscountPerLiter ?? Math.Max(0, fuel.BasePrice - fuel.DiscountPrice),
            // base_price is the pump/reference price; the actual customer discount
            // is base - final. Derived here (display only) so the admin UI can show it.
            DiscountPerLiter = Math.Max(0, fuel.BasePrice - fuel.DiscountPrice),
            AllowBelowCost = fuel.AllowBelowCost,
            PackageLiters = packages.Select(p => (int)p.Liters).OrderBy(l => l).ToList(),
            PriceSpread = BuildSpread(supplierCosts, finals)
        };
    }

    /// <summary>
    /// Null when every package agrees - the ordinary case, and the one that must stay quiet. Set
    /// when they do not, covering the two figures that decide sellability: what we paid, and what
    /// the customer pays.
    /// </summary>
    private static ProviderFuelPriceSpreadDto? BuildSpread(List<decimal> supplierCosts, List<decimal> finals)
    {
        var costsDisagree = supplierCosts.Count > 1 && supplierCosts.Min() != supplierCosts.Max();
        var finalsDisagree = finals.Count > 1 && finals.Min() != finals.Max();

        if (!costsDisagree && !finalsDisagree) return null;

        return new ProviderFuelPriceSpreadDto
        {
            MinSupplierPricePerLiter = supplierCosts.Count > 0 ? supplierCosts.Min() : 0m,
            MaxSupplierPricePerLiter = supplierCosts.Count > 0 ? supplierCosts.Max() : 0m,
            MinFinalPricePerLiter = finals.Count > 0 ? finals.Min() : 0m,
            MaxFinalPricePerLiter = finals.Count > 0 ? finals.Max() : 0m
        };
    }
}