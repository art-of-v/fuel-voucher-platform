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
        var pkg = new FuelPackage
        {
            Id = "p", StationId = "okko", FuelTypeId = "okko-95", FuelName = "A-95",
            Liters = 10m,
            Price = 9999,                    // stale frozen price must be ignored
            SupplierPricePerLiter = 49m,     // cost
            MarginUahPerLiter = 7m,          // profit → cost-plus 56
            PumpPricePerLiter = 55m,
            MinDiscountPerLiter = 0.5m,      // ceiling 54.5 binds
            FinalPricePerLiter = 56m,        // stale stored final must be ignored
        };

        // 54.5 * 10 = 545
        ServerPricing.PackagePrice(pkg, 10m).Should().Be(545);
    }

    [Fact]
    public void PackagePrice_NullKnobs_FallsBackToStoredFinal()
    {
        var pkg = new FuelPackage
        {
            Id = "p", StationId = "okko", FuelTypeId = "okko-95", FuelName = "A-95",
            Liters = 10m, Price = 9999,
            SupplierPricePerLiter = null,
            MarginUahPerLiter = null,
            FinalPricePerLiter = 51m,
        };

        ServerPricing.PackagePrice(pkg, 10m).Should().Be(510);
    }

    [Fact]
    public void PackagePrice_NoPerLiterData_UsesFrozenPrice()
    {
        var pkg = new FuelPackage
        {
            Id = "p", StationId = "okko", FuelTypeId = "okko-95", FuelName = "A-95",
            Liters = 10m, Price = 510,
            SupplierPricePerLiter = null,
            MarginUahPerLiter = null,
            FinalPricePerLiter = null,
        };

        ServerPricing.PackagePrice(pkg, 10m).Should().Be(510);
    }

    // --- ServerPricing.IsBelowCost -----------------------------------------

    /// <summary>
    /// The regression this covers: a package with a recorded cost but no stored per-litre price used to
    /// fall back to <c>Price</c> — the frozen PACKAGE TOTAL — and compare it against a per-litre cost.
    /// 590 UAH for 50 L read as "590 vs 54" and looked profitable while the customer was being charged
    /// 11.80/litre against a 54/litre cost: a 42 UAH/litre loss on every litre, guard green.
    /// </summary>
    [Fact]
    public void IsBelowCost_NeverComparesThePackageTotalAgainstAPerLiterCost()
    {
        var pkg = new FuelPackage
        {
            Id = "p", StationId = "okko", FuelTypeId = "okko-95", FuelName = "A-95",
            Liters = 50m,
            Price = 590m,                    // package TOTAL — a different unit entirely
            SupplierPricePerLiter = 54m,     // per-litre cost
            MarginUahPerLiter = null,        // ...and no per-litre price to be derived from
            FinalPricePerLiter = null,
            PumpPricePerLiter = 92.90m,
        };

        // What the customer is actually charged, per litre:
        (ServerPricing.PackagePrice(pkg, 50m) / 50m).Should().BeLessThan(54m);

        ServerPricing.IsBelowCost(pkg, 0m).Should().BeFalse(
            "the guard cannot prove a loss from a per-litre cost and a package total");
        ServerPricing.TryGetBelowCostVerdict(pkg, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.UnknownCost,
                "and it must say so rather than pass silently");
    }

    [Fact]
    public void TryGetBelowCostVerdict_ShouldReportBelowCost_WhenTheChargeIsUnderTheCost()
    {
        var pkg = new FuelPackage
        {
            Id = "p", StationId = "okko", FuelTypeId = "okko-95", FuelName = "A-95",
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
        var pkg = new FuelPackage
        {
            Id = "p", StationId = "okko", FuelTypeId = "okko-95", FuelName = "A-95",
            Liters = 10m,
            SupplierPricePerLiter = cost is null ? null : (decimal)cost.Value,
            MarginUahPerLiter = 2m,
            FinalPricePerLiter = 52m,
        };

        ServerPricing.TryGetBelowCostVerdict(pkg, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.UnknownCost);
        ServerPricing.IsBelowCost(pkg, 0m).Should().BeFalse();
    }

    [Fact]
    public void TryGetBelowCostVerdict_ShouldReportAboveCost_ForASoundlyPricedLine()
    {
        var pkg = new FuelPackage
        {
            Id = "p", StationId = "okko", FuelTypeId = "okko-95", FuelName = "A-95",
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
        var pkg = new FuelPackage
        {
            Id = "p", StationId = "okko", FuelTypeId = "okko-95", FuelName = "A-95",
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
