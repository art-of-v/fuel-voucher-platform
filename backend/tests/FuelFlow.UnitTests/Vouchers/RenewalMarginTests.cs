using FluentAssertions;
using FuelFlow.Features.Vouchers.Renewal;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// A renewal comes in two branches and only one of them can lose money, so the guard is scoped to
/// that one. Extend keeps the same voucher and only pushes its expiry out: no asset leaves the
/// warehouse, and the payment is credited against the voucher's residual cost instead. Replace hands
/// over stock we paid for, so a fee under that cost is money leaving.
/// </summary>
public sealed class RenewalMarginTests
{
    [Fact]
    public void Replace_CoveringTheCost_IsNotALoss()
    {
        // 50 L of stock costing 45/litre = 2250. A 2500 fee clears it with 250 to spare.
        RenewalMargin.ForReplacement(amountCollectedUah: 2500m, liters: 50m, costPerLiter: 45m)
            .Should().Be(RenewalMarginVerdict.AtOrAboveCost);
    }

    [Fact]
    public void Replace_ExactlyCoveringTheCost_IsNotALoss()
    {
        // Equal is not a loss. Releasing near-expiry stock at zero margin is a real tactic.
        RenewalMargin.ForReplacement(amountCollectedUah: 2250m, liters: 50m, costPerLiter: 45m)
            .Should().Be(RenewalMarginVerdict.AtOrAboveCost);
    }

    [Fact]
    public void Replace_UnderTheCost_IsALoss_AndTheShortfallIsQuantified()
    {
        // The tier was configured below what the replacement stock costs: every renewal of this term
        // lost money, repeatably, and nothing in the flow compared the two numbers.
        var verdict = RenewalMargin.ForReplacement(
            amountCollectedUah: 1000m, liters: 50m, costPerLiter: 45m);

        verdict.Should().Be(RenewalMarginVerdict.BelowCost);
        RenewalMargin.ShortfallUah(1000m, 50m, 45m).Should().Be(1250m);
    }

    [Theory]
    // A missing or zero cost means "never recorded", not "free". Treating it as free would read every
    // uncosted stock as a windfall and hide the loss once the cost is finally entered.
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Replace_WithoutARecordedCost_SaysUnknown_RatherThanAssumingFree(double? cost)
    {
        RenewalMargin.ForReplacement(
            amountCollectedUah: 1m,
            liters: 50m,
            costPerLiter: cost is null ? null : (decimal)cost.Value)
            .Should().Be(RenewalMarginVerdict.UnknownCost);
    }

    [Fact]
    public void Replace_WithNoLiters_SaysUnknown_BecauseThereIsNothingToScaleCostTo()
    {
        RenewalMargin.ForReplacement(amountCollectedUah: 100m, liters: 0m, costPerLiter: 45m)
            .Should().Be(RenewalMarginVerdict.UnknownCost);
    }

    [Fact]
    public void ShortfallUah_ShouldBeZero_WhenThereIsNoLoss()
    {
        // The operator-facing message quotes this number; it must not claim a loss on a sound sale.
        RenewalMargin.ShortfallUah(2500m, 50m, 45m).Should().Be(0m);
        RenewalMargin.ShortfallUah(100m, 50m, null).Should().Be(0m);
    }

    [Fact]
    public void Replace_ShouldCompareAgainstThePerLitreCostTimesTheNominal()
    {
        // A small nominal must not be judged on the per-litre rate alone: 10 L of 45/litre costs 450,
        // and a 400 fee is under it even though 40/litre looks close to the 45 rate.
        RenewalMargin.ForReplacement(amountCollectedUah: 400m, liters: 10m, costPerLiter: 45m)
            .Should().Be(RenewalMarginVerdict.BelowCost);
        RenewalMargin.ShortfallUah(400m, 10m, 45m).Should().Be(50m);
    }

    /// <summary>
    /// The customer's voucher is not scrap — it comes back into our sellable pool, at what we paid for
    /// it. Judging the fee alone called a 300 fee against a 500 replacement a 200 loss, when the
    /// exchange is really 300 + 500 against 500.
    /// </summary>
    [Fact]
    public void Replace_BelowCostOnTheFeeAlone_IsFine_WhenTheIncomingVoucherCoversIt()
    {
        RenewalMargin.ForReplacement(
            amountCollectedUah: 300m, liters: 10m, costPerLiter: 50m, incomingCostPerLiter: 50m)
            .Should().Be(RenewalMarginVerdict.AtOrAboveCost);
    }

    [Fact]
    public void Shortfall_ShouldAccountForTheIncomingVoucher()
    {
        // 100 fee against a 500 replacement, incoming voucher worth 300: short by 500 - 100 - 300.
        RenewalMargin.ShortfallUah(300m, 10m, 50m, 30m).Should().Be(0m);
        RenewalMargin.ShortfallUah(100m, 10m, 50m, 30m).Should().Be(100m);
    }

    [Fact]
    public void Replace_StillRefused_WhenTheIncomingVoucherDoesNotCoverTheGap()
    {
        // A 500 voucher coming back is real money, but it is not a free pass: fee 300 + credit 500 is
        // 800 against a 900 replacement, so 100 genuinely leaves.
        var verdict = RenewalMargin.ForReplacement(
            amountCollectedUah: 300m, liters: 10m, costPerLiter: 90m, incomingCostPerLiter: 50m);

        verdict.Should().Be(RenewalMarginVerdict.BelowCost);
        RenewalMargin.ShortfallUah(300m, 10m, 90m, 50m).Should().Be(100m);
    }

    [Fact]
    public void Replace_IsAtOrAbove_WhenFeePlusIncomingVoucher_CoversTheReplacement()
    {
        // The same exchange as the case above with a 700 replacement: 300 + 500 = 800 clears it.
        // The credit is what turns a refusal into a sale, so both halves of the boundary are asserted.
        RenewalMargin.ForReplacement(
            amountCollectedUah: 300m, liters: 10m, costPerLiter: 70m, incomingCostPerLiter: 50m)
            .Should().Be(RenewalMarginVerdict.AtOrAboveCost);
    }

    [Theory]
    // An unpriced incoming voucher credits nothing. Assuming the value of an asset we cannot price is
    // exactly how a real loss hides, so the conservative reading wins.
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Replace_WithAnUnpricedIncomingVoucher_CreditsNothing(double? incoming)
    {
        var incomingDecimal = incoming is null ? null : (decimal?)incoming.Value;

        RenewalMargin.ForReplacement(
            amountCollectedUah: 300m, liters: 10m, costPerLiter: 70m, incomingCostPerLiter: incomingDecimal)
            .Should().Be(RenewalMarginVerdict.BelowCost);
        RenewalMargin.ShortfallUah(300m, 10m, 70m, incomingDecimal).Should().Be(400m);
    }

    [Fact]
    public void Replace_WithoutARecordedReplacementCost_SaysUnknown_EvenWithAValuableIncomingVoucher()
    {
        // "Unknown" is about the thing we cannot judge — what we hand over. A rich voucher coming back
        // does not make an unpriced replacement sellable, and must not report a loss either.
        RenewalMargin.ForReplacement(
            amountCollectedUah: 1m, liters: 10m, costPerLiter: null, incomingCostPerLiter: 90m)
            .Should().Be(RenewalMarginVerdict.UnknownCost);
        RenewalMargin.ShortfallUah(1m, 10m, null, 90m).Should().Be(0m);
    }

    [Fact]
    public void Replace_WithNoIncomingVoucher_BehavesExactlyAsBefore()
    {
        // The credit is optional, so a caller that has nothing to credit gets the original verdict.
        RenewalMargin.ForReplacement(300m, 10m, 50m).Should().Be(RenewalMarginVerdict.BelowCost);
        RenewalMargin.ForReplacement(600m, 10m, 50m).Should().Be(RenewalMarginVerdict.AtOrAboveCost);
        RenewalMargin.ShortfallUah(300m, 10m, 50m).Should().Be(200m);
    }
}