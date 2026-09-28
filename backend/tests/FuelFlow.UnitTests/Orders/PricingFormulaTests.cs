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
}
