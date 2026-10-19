using FluentAssertions;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.UnitTests.Orders;

/// <summary>
/// Pricing epic slice 1a: final/л = min(cost + profit, pump − minDiscount), with a
/// null-pump fallback to cost + profit. Covers the shared <see cref="FuelPricing"/>
/// formula and the checkout <see cref="ServerPricing"/> that recomputes from it.
/// </summary>
public sealed class PricingFormulaTests
{
    // --- FuelPricing.FinalPerLiterOrNull -----------------------------------
    //
    // The nullable wrapper, which is what every read path calls now that the offer carries no stored
    // final price. Its contract has two halves and the second is the one that matters: "no price" is
    // null, not a number built from whatever happens to be present.

    [Theory]
    [InlineData(null, 2.0, null)]
    [InlineData(49.0, null, null)]
    [InlineData(null, null, null)]
    public void FinalPerLiterOrNull_NoCostOrNoMargin_IsNotAPrice(double? cost, double? margin, double? pump)
    {
        var result = FuelPricing.FinalPerLiterOrNull(
            cost is null ? null : (decimal)cost.Value,
            margin is null ? null : (decimal)margin.Value,
            pump is null ? null : (decimal)pump.Value,
            0m);

        // Treating a missing margin as zero would publish a fuel for free; treating a missing cost as
        // zero would let the below-cost guard see a cost of zero and wave the sale through.
        result.Should().BeNull();
    }

    [Fact]
    public void FinalPerLiterOrNull_Complete_ComputesTheSameAsTheFormula()
    {
        // cost-plus, and the same inputs through the non-nullable overload: the wrapper decides whether
        // there is a price, it does not have its own arithmetic.
        FuelPricing.FinalPerLiterOrNull(49m, 2m, 55m, 0.5m)
            .Should().Be(FuelPricing.FinalPerLiter(49m, 2m, 55m, 0.5m));

        // Ceiling binding: 49 + 2 = 51 against 55 - 4 = 51 is equal here, so use a binding case.
        FuelPricing.FinalPerLiterOrNull(49m, 2m, 50m, 0m)
            .Should().Be(50m);
    }

    [Fact]
    public void FinalPerLiterOrNull_WithoutAPump_FallsBackToCostPlus()
    {
        FuelPricing.FinalPerLiterOrNull(49m, 2m, null, 0m).Should().Be(51m);

        // And a missing min-discount is zero, not an error: the column is nullable and a fuel priced
        // before discounts existed has none.
        FuelPricing.FinalPerLiterOrNull(49m, 2m, null, null).Should().Be(51m);
    }

    // --- FuelPricing.FinalPerLiter -----------------------------------------

    [Fact]
    public void FinalPerLiter_CostPlusBelowCeiling_ReturnsCostPlus()
    {
        // cost 49 + profit 2 = 51; ceiling 55 - 0.5 = 54.5 → cost-plus wins.
        FuelPricing.FinalPerLiter(49m, 2m, 55m, 0.5m).Should().Be(51m);
    }

    [Fact]
    public void FinalPerLiter_CeilingBelowCostPlus_ReturnsCeiling()
    {
        // cost 49 + profit 5 = 54; ceiling 55 - 0.5 = 54.5 → still cost-plus.
        FuelPricing.FinalPerLiter(49m, 5m, 55m, 0.5m).Should().Be(54m);
        // cost 49 + profit 7 = 56; ceiling 55 - 0.5 = 54.5 → ceiling caps it.
        FuelPricing.FinalPerLiter(49m, 7m, 55m, 0.5m).Should().Be(54.5m);
    }

    [Fact]
    public void FinalPerLiter_NullPump_FallsBackToCostPlus()
    {
        FuelPricing.FinalPerLiter(49m, 2m, null, 0.5m).Should().Be(51m);
        // minDiscount is irrelevant without a pump ceiling.
        FuelPricing.FinalPerLiter(49m, 2m, null, 5m).Should().Be(51m);
    }

    [Fact]
    public void FinalPerLiter_PumpMinusDiscountBelowCost_ReturnsBelowCost()
    {
        // Below-cost guarding is deferred to slice 3; the pure formula still returns
        // the ceiling even when it dips under cost so the math stays predictable.
        FuelPricing.FinalPerLiter(50m, 2m, 49m, 0.5m).Should().Be(48.5m);
    }

    // --- FuelPricing.IsBelowCost -------------------------------------------

    [Fact]
    public void IsBelowCost_CeilingForcesPriceUnderCost_IsTrue()
    {
        // cost 50, cost-plus 52, but pump 49 − 0.5 = 48.5 binds → final 48.5 < 50.
        FuelPricing.IsBelowCost(50m, 2m, 49m, 0.5m).Should().BeTrue();
    }

    [Fact]
    public void IsBelowCost_PriceAtOrAboveCost_IsFalse()
    {
        // cost-plus 51 ≥ cost 49, no ceiling binding.
        FuelPricing.IsBelowCost(49m, 2m, 55m, 0.5m).Should().BeFalse();
        // final exactly equals cost is not "below".
        FuelPricing.IsBelowCost(50m, 0m, 55m, 0m).Should().BeFalse();
    }

    [Fact]
    public void IsBelowCost_NullPump_NeverBelow()
    {
        // No ceiling → final = cost + profit ≥ cost, so it can never dip under.
        FuelPricing.IsBelowCost(49m, 2m, null, 5m).Should().BeFalse();
    }

    [Fact]
    public void IsBelowCost_ZeroOrUnknownCost_IsFalse()
    {
        // With no supplier cost recorded there is nothing to sell below.
        FuelPricing.IsBelowCost(0m, 2m, 1m, 0m).Should().BeFalse();
    }

    // --- FuelPricing.OriginalPackagePrice ----------------------------------

    [Fact]
    public void OriginalPackagePrice_PumpSet_ReturnsPumpTimesLitersRounded()
    {
        // Pump × liters is the public "before"/struck price; the stored sale price is ignored.
        FuelPricing.OriginalPackagePrice(60m, 10m, 500).Should().Be(600m);
        // Fractional pump × liters now keeps kopecks (#90 decimal money): 55.06 × 3 = 165.18,
        // rounded to 2 dp, no longer collapsed to a whole 165 UAH.
        FuelPricing.OriginalPackagePrice(55.06m, 3m, 0).Should().Be(165.18m);
    }

    [Fact]
    public void OriginalPackagePrice_NullPump_FallsBackToSalePrice()
    {
        // No pump recorded → "before" == sale price, so the store-front shows no fabricated saving
        // and never a cost-derived number (planning #73).
        FuelPricing.OriginalPackagePrice(null, 10m, 500).Should().Be(500);
        FuelPricing.OriginalPackagePrice(null, 999m, 500).Should().Be(500);
    }

    // --- ServerPricing.PackagePrice ----------------------------------------

    [Fact]
    public void PackagePrice_RecomputesFromKnobs_WhenCeilingBinds()
    {
        var pkg = new SellablePackage
        {
            Liters = 10m,
            SupplierPricePerLiter = 49m,     // cost
            MarginUahPerLiter = 7m,          // profit → cost-plus 56
            PumpPricePerLiter = 55m,
            MinDiscountPerLiter = 0.5m,      // ceiling 54.5 binds
            // No stale stored final to ignore any more: the offer stopped carrying one on 2026-10-17.
            // What this pins is that the knobs, and only the knobs, decide the charge.
        };

        // 54.5 * 10 = 545
        ServerPricing.PackagePrice(pkg, 10m).Should().Be(545);
    }

[Fact]
public void PackagePrice_NoKnobs_IsRefusedRatherThanCharged()
{
    var pkg = new SellablePackage
    {
        Liters = 10m,
        SupplierPricePerLiter = null,
        MarginUahPerLiter = null,
    };

    // No rate to price from. Returning zero here would put a free fuel on the
    // shelf; the checkout handlers resolve the package before pricing it and never reach this -
    // throwing says "this cannot be sold" instead of quietly charging nothing.
    var act = () => ServerPricing.PackagePrice(pkg, 10m);
    act.Should().Throw<NoPriceAvailableException>();
}

[Fact]
public void TryPackagePrice_NoKnobs_IsNullRatherThanAFault()
{
    // The nullable twin, for the callers that can meet such a fuel and have to decide what to show:
    // the term quote answers with the ladder and no prices instead of faulting the customer's screen.
    // One arithmetic, two shapes - the number is the same one the throwing form would have returned.
    var pkg = new SellablePackage
    {
        Liters = 10m,
        SupplierPricePerLiter = null,
        MarginUahPerLiter = null,
        PumpPricePerLiter = 92.90m,
    };

    ServerPricing.TryPackagePrice(pkg, 10m, 0m).Should().BeNull();
    ServerPricing.DiscountedPricePerLiter(pkg, 20m).Should().BeNull();
}

[Fact]
public void TryPackagePrice_WithKnobs_IsTheSameNumberTheThrowingFormReturns()
{
    // A quoted price and a charged price that disagree are the bug this pair exists to prevent.
    var pkg = new SellablePackage
    {
        Liters = 10m,
        SupplierPricePerLiter = 49m,
        MarginUahPerLiter = 7m,
        PumpPricePerLiter = 55m,
        MinDiscountPerLiter = 0.5m,
    };

    // 54.5 * 10 = 545, and with a 5 ₴/L term discount off the per-litre price: 49.5 * 10 = 495.
    ServerPricing.TryPackagePrice(pkg, 10m, 0m).Should().Be(ServerPricing.PackagePrice(pkg, 10m));
    ServerPricing.TryPackagePrice(pkg, 10m, 5m).Should().Be(ServerPricing.PackagePrice(pkg, 10m, 5m));
}

// --- ServerPricing.IsBelowCost -----------------------------------------

    /// <summary>
    /// The regression this covers: a package whose stored total once fell back to <c>Price</c> — the
    /// frozen PACKAGE TOTAL — and compared it against a per-litre cost. 590 UAH for 50 L read as
    /// "590 vs 54" and looked profitable while the customer was being charged 11.80/litre against a
    /// 54/litre cost: a 42 UAH/litre loss on every litre, guard green.
    /// <para>
    /// There is no stored total any more — the charge is always the rate x litres, capped by the
    /// ceiling. This pins that the guard judges the same number the customer is charged, per litre
    /// against per litre, and refuses the sale when the ceiling lands under cost.
    /// </para>
    /// </summary>
    [Fact]
    public void IsBelowCost_NeverComparesThePackageTotalAgainstAPerLiterCost()
    {
        var pkg = new SellablePackage
        {
            Liters = 50m,
            SupplierPricePerLiter = 54m,             // per-litre cost
            MarginUahPerLiter = 7m,                 // cost-plus would be 61
            PumpPricePerLiter = 55m,                // ceiling 55 - 5 = 50, under the 54 cost
            MinDiscountPerLiter = 5m,
        };

        // What the customer is actually charged, per litre:
        (ServerPricing.PackagePrice(pkg, 50m) / 50m).Should().BeLessThan(54m);

        // 2500 for 50 L against a cost of 54/litre is a 4 UAH/litre loss and is refused. Per litre
        // against per litre — the mistake this test has always covered was comparing the total to
        // the rate.
        ServerPricing.IsBelowCost(pkg, 0m).Should().BeTrue();
        ServerPricing.TryGetBelowCostVerdict(pkg, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.BelowCost);
    }

    [Fact]
    public void IsBelowCost_APriceAtOrAboveCost_IsFine()
    {
        var pkg = new SellablePackage
        {
            Liters = 10m,
            SupplierPricePerLiter = 49m,
            MarginUahPerLiter = 2m,
        };

        ServerPricing.TryGetBelowCostVerdict(pkg, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.AboveCost);
    }

    [Fact]
    public void TryGetBelowCostVerdict_ShouldReportBelowCost_WhenTheChargeIsUnderTheCost()
    {
        var pkg = new SellablePackage
        {
            Liters = 10m,
            SupplierPricePerLiter = 50m,
            MarginUahPerLiter = 2m,
            PumpPricePerLiter = 40m,   // ceiling 39 < cost 50
            MinDiscountPerLiter = 0m,
        };

        ServerPricing.TryGetBelowCostVerdict(pkg, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.BelowCost);
        ServerPricing.IsBelowCost(pkg, 0m).Should().BeTrue();
    }

    [Theory]
    // A cost of 0 means "never recorded", not "free". Reading it as free would make every line look
    // loss-making and block every sale of a fuel nobody has costed yet.
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void TryGetBelowCostVerdict_ShouldReportUnknown_WhenNoRealCostIsRecorded(double? cost)
    {
        var pkg = new SellablePackage
        {
            Liters = 10m,
            SupplierPricePerLiter = cost is null ? null : (decimal)cost.Value,
            MarginUahPerLiter = 2m,
        };

        ServerPricing.TryGetBelowCostVerdict(pkg, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.UnknownCost);
        ServerPricing.IsBelowCost(pkg, 0m).Should().BeFalse();
    }

    [Fact]
    public void TryGetBelowCostVerdict_ShouldReportAboveCost_ForASoundlyPricedLine()
    {
        var pkg = new SellablePackage
        {
            Liters = 10m,
            SupplierPricePerLiter = 40m,
            MarginUahPerLiter = 5m,
            PumpPricePerLiter = 92.90m,
            MinDiscountPerLiter = 1m,
        };

        ServerPricing.TryGetBelowCostVerdict(pkg, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.AboveCost);
    }

    [Fact]
    public void TryGetBelowCostVerdict_ShouldSeeTheTermDiscount()
    {
        // 59/litre over a 50 cost is fine; a 10/litre term discount takes it to 49 and under. The guard
        // has to judge the figure actually charged or a generous tier sells under cost unnoticed.
        var pkg = new SellablePackage
        {
            Liters = 10m,
            SupplierPricePerLiter = 50m,
            MarginUahPerLiter = 9m,
            PumpPricePerLiter = 92.90m,
            MinDiscountPerLiter = 1m,
        };

        ServerPricing.TryGetBelowCostVerdict(pkg, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.AboveCost);
        ServerPricing.TryGetBelowCostVerdict(pkg, 10m)
            .Should().Be(ServerPricing.BelowCostVerdict.BelowCost);
    }
}
