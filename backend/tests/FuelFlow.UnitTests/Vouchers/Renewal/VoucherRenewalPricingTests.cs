using FluentAssertions;
using FuelFlow.Features.Vouchers.Renewal;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers.Renewal;

public sealed class VoucherRenewalPricingTests
{
    [Theory]
    [InlineData(50, 2.5, 125)]
    [InlineData(20, 3, 60)]
    [InlineData(0, 5, 0)]
    [InlineData(10, 0, 0)]
    public void LineAmountUah_ShouldScaleLinearly(decimal liters, decimal rate, int expected)
    {
        VoucherRenewalPricing.LineAmountUah(liters, rate).Should().Be(expected);
    }

    [Fact]
    public void LineAmountUah_ShouldRoundToKopeck_HalfAwayFromZero()
    {
        // #90 decimal money: amounts keep kopecks, rounding to 2 dp (not whole UAH), half away from zero.
        // InlineData cannot hold decimal literals, so the fractional cases live in a [Fact] body.
        VoucherRenewalPricing.LineAmountUah(1m, 10.125m).Should().Be(10.13m); // .125 -> away from zero -> .13
        VoucherRenewalPricing.LineAmountUah(1m, 10.124m).Should().Be(10.12m); // .124 -> .12
        VoucherRenewalPricing.LineAmountUah(3m, 3.5m).Should().Be(10.5m);     // 10.50, kopecks preserved
        VoucherRenewalPricing.LineAmountUah(10m, 1.05m).Should().Be(10.5m);   // 10.50, no longer rounds up to a whole 11
    }

    [Fact]
    public void LineAmountUah_ShouldReflectTermDiscount_ShorterIsCheaper()
    {
        // Same nominal: a shorter term's lower per-litre rate must cost less.
        var cheaper = VoucherRenewalPricing.LineAmountUah(50m, 2m);
        var dearer = VoucherRenewalPricing.LineAmountUah(50m, 3m);
        cheaper.Should().BeLessThan(dearer);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(10, -1)]
    public void LineAmountUah_ShouldThrow_OnNegativeInput(decimal liters, decimal rate)
    {
        var act = () => VoucherRenewalPricing.LineAmountUah(liters, rate);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TotalUah_ShouldSumPerVoucherLines_EachWithItsOwnRate()
    {
        var lines = new (decimal Liters, decimal RatePerLiterUah)[]
        {
            (50m, 2.5m),  // 125.00
            (20m, 3m),    // 60.00
            (10m, 1.05m)  // 10.50 (kopecks preserved per line, #90 — was rounded to a whole 11 before)
        };

        VoucherRenewalPricing.TotalUah(lines).Should().Be(195.50m);
    }

    [Fact]
    public void TotalUah_ShouldBeZero_ForEmptyBatch()
    {
        VoucherRenewalPricing.TotalUah(Array.Empty<(decimal, decimal)>()).Should().Be(0);
    }
}
