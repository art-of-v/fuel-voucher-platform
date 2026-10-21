using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands;

/// <summary>
/// Builds a fuel's pricing card. Shared by <c>/providers</c> and
/// <c>/providers/{id}</c> so the two can never describe the same fuel differently.
/// </summary>
/// <remarks>
/// <para>
/// The pricing is read off the <b>fuel row</b>, because that is where it lives: since
/// <c>20261019095748_OfferPricingBecomesTheFuel</c> the cost, the margin and the pump price are
/// columns on <c>fuel_types</c>, one set per fuel. The minimum discount that used to stand beside
/// them was the last piece of the old formula and is gone (2026-10-21).
/// </para>
/// <para>
/// It used to be read off the <em>packages</em> - the fuel's sizes - which was correct while the
/// pricing lived on the offer and each size carried its own copy. It stopped being correct the
/// moment the pricing moved onto the fuel, and the symptom was a fuel the catalogue import had just
/// created showing an empty card: a freshly imported fuel has no size yet, so there was no package
/// to read the numbers from, and the card said <c>0.00</c> and <c>-</c> while the row held the
/// pump price the import had just written. The card was asking the wrong question - "what does the
/// smallest size cost" - when the only question now is "what does this fuel cost".
/// </para>
/// <para>
/// The packages are still read, for the two things only they know: the sizes the operator may sell
/// and whether the stock is the rehearsal pool.
/// </para>
/// </remarks>
internal static class BrandFuelCard
{
    /// <summary>
    /// The nominal the remaining per-package figures are read from: the smallest one, ties broken by
    /// id. Deterministic, and the nominal an operator reads the card as ("what does a litre of this
    /// cost") is the smallest one. When packages disagree, <see cref="BrandFuelDto.PriceSpread"/>
    /// is what makes that visible.
    /// </summary>
    private static SellablePackage? Reference(IReadOnlyList<SellablePackage> packages) =>
        packages
            .OrderBy(p => p.Liters)
            .ThenBy(p => p.PackageId, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// What the customer saves against the column price, per litre.
    /// </summary>
    /// <remarks>
    /// The column price is what the pump says; the customer price is what they pay; the saving is
    /// the difference. **Both numbers are computed**: the column price is the fuel's pump price, and
    /// the customer price is the formula's own answer. A fuel nobody has priced saves nothing,
    /// because nothing has been promised - and a fuel whose price is unknown is not a fuel priced at
    /// zero, which is the "a price of zero is a price" trap showing up on a screen.
    /// </remarks>
    private static decimal CustomerSavingPerLiter(FuelTypeEntity fuel)
    {
        if (fuel.PumpPricePerLiter is not { } column || fuel.FinalPerLiter() is not { } price)
        {
            return 0m;
        }

        return Math.Max(0, column - price);
    }

    public static BrandFuelDto Build(FuelTypeEntity fuel, IReadOnlyList<SellablePackage> packages)
    {
        var reference = Reference(packages);

        var supplierCosts = packages
            .Where(p => p.SupplierPricePerLiter is not null)
            .Select(p => p.SupplierPricePerLiter!.Value)
            .ToList();
        var finals = packages
            .Where(p => p.ComputedFinalPricePerLiter is not null)
            .Select(p => p.ComputedFinalPricePerLiter!.Value)
            .ToList();

        return new BrandFuelDto
        {
            Id = fuel.Id.ToString(),
            Name = fuel.Name,
            // What we pay. The column is the truth, and the pool's blended cost is written back into
            // it by BlendedCostRecalculator when a batch of vouchers is costed - so the card shows
            // the column until a position is imported at another price, and the column's new value
            // afterwards. Reading it through the sizes instead (as this card used to) averaged the
            // fuel's own figure over the fuel's own sizes, which is the same number on a fuel that
            // has any, and nothing at all on one that has none.
                // NULL, not zero. A zero here would be written straight back to the fuel by the save
                // that posts this card, and a fuel with a cost of zero is priced at its margin and has
                // no below-cost guard. "We do not know what we paid" is a fact the screen can show; a
                // zero is a lie the database would believe.
                SupplierPricePerLiter = fuel.SupplierPricePerLiter,
                MarginUahPerLiter = fuel.MarginUahPerLiter,
            MarginPercent = fuel.MarginPercent,
            // The price the customer pays: ours when we can compute it, and otherwise the column
            // price the catalogue import published. A zero on this card is not "free", it is "we do
            // not know yet" - and the column price is a better answer than that, and the one the
            // operator is looking at anyway.
            FinalPricePerLiter = fuel.FinalPerLiter() ?? fuel.PumpPricePerLiter ?? 0m,
            PumpPricePerLiter = fuel.PumpPricePerLiter,
            // The reference is the column price, and the discount is the difference between it and
            // the price the customer pays. Both computed: the card no longer reads a stored copy of
            // either, because the copies were four writers' opinions about one number.
            DiscountPerLiter = CustomerSavingPerLiter(fuel),
            AllowBelowCost = fuel.AllowBelowCost,
            // Read off the fuel, not derived from the packages. A code is how the catalogue import
            // recognises this grade on the next tick, so an operator cannot see it without knowing
            // which of their fuels will still match after a sync. It was absent here, so
            // GET /api/admin/brands serialised null for every fuel and the screen showed nothing.
            BrandFuelCode = fuel.BrandFuelCode,
            PackageLiters = packages.Select(p => (int)p.Liters).OrderBy(l => l).ToList(),
            // Read back off the packages rather than a column on the fuel type, because the flag
            // lives on the package - that is the row the storefront filters. Any=true when the fuel
            // has packages and they are all flagged; a mixed fuel shows flagged, since the operator
            // needs to look at it either way.
            IsTestData = packages.Count > 0 && packages.All(p => p.DataKind == DataKinds.Qa),
            PriceSpread = BuildSpread(supplierCosts, finals)
        };
    }

    /// <summary>
    /// Null when every package agrees - the ordinary case, and the one that must stay quiet. Set
    /// when they do not, covering the two figures that decide sellability: what we paid, and what
    /// the customer pays.
    /// <para>
    /// Since 2026-10-17 the two columns live once per offer, so within one fuel's own pool the values
    /// <b>cannot</b> disagree and the final-price half of this detector is structurally always null. It
    /// is kept because the collection it is handed is not trusted to be one pool: it still catches a
    /// caller that merged pools, and it costs two comparisons. What it can no longer catch is the drift
    /// it was written for - two copies of one number disagreeing - because there is only one copy now.
    /// </para>
    /// </summary>
    private static BrandFuelPriceSpreadDto? BuildSpread(List<decimal> supplierCosts, List<decimal> finals)
    {
        var costsDisagree = supplierCosts.Count > 1 && supplierCosts.Min() != supplierCosts.Max();
        var finalsDisagree = finals.Count > 1 && finals.Min() != finals.Max();

        if (!costsDisagree && !finalsDisagree) return null;

        return new BrandFuelPriceSpreadDto
        {
            MinSupplierPricePerLiter = supplierCosts.Count > 0 ? supplierCosts.Min() : 0m,
            MaxSupplierPricePerLiter = supplierCosts.Count > 0 ? supplierCosts.Max() : 0m,
            MinFinalPricePerLiter = finals.Count > 0 ? finals.Min() : 0m,
            MaxFinalPricePerLiter = finals.Count > 0 ? finals.Max() : 0m
        };
    }
}