using FluentAssertions;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class PurchaseBatchCostingTests
{
    [Fact]
    public void BlendedCost_EmptyPool_ReturnsNull()
    {
        PurchaseBatchCosting.BlendedCostPerLiter([]).Should().BeNull();
    }

    [Fact]
    public void BlendedCost_SingleBatch_ReturnsItsCost()
    {
        PurchaseBatchCosting.BlendedCostPerLiter([(100m, 22m)]).Should().Be(22m);
    }

    [Fact]
    public void BlendedCost_EqualLiters_IsPlainAverage()
    {
        // (100×20 + 100×30) / 200 = 25
        PurchaseBatchCosting.BlendedCostPerLiter([(100m, 20m), (100m, 30m)]).Should().Be(25m);
    }

    [Fact]
    public void BlendedCost_WeightsByRemainingLiters()
    {
        // Textbook perpetual moving average: batch1 100L@20 with 50 sold (50 remain) + batch2 100L@26.
        // (50×20 + 100×26) / 150 = 3600/150 = 24.
        PurchaseBatchCosting.BlendedCostPerLiter([(50m, 20m), (100m, 26m)]).Should().Be(24m);
    }

    [Fact]
    public void BlendedCost_IgnoresZeroAndNegativeLiters()
    {
        // A fully-sold batch (0 remaining) or bad row must not skew or divide the average.
        PurchaseBatchCosting.BlendedCostPerLiter([(0m, 20m), (-5m, 999m), (100m, 26m)]).Should().Be(26m);
    }

    [Fact]
    public void BlendedCost_OnlyZeroLiters_ReturnsNull()
    {
        PurchaseBatchCosting.BlendedCostPerLiter([(0m, 20m), (0m, 30m)]).Should().BeNull();
    }
}
