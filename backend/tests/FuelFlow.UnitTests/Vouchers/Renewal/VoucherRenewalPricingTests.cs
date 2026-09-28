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

    [Theory]
    [InlineData(10, 1.05, 11)] // 10.5 -> away from zero -> 11
    [InlineData(10, 1.04, 10)] // 10.4 -> 10
    [InlineData(3, 3.5, 11)]   // 10.5 -> 11
    public void LineAmountUah_ShouldRoundHalfAwayFromZero(decimal liters, decimal rate, int expected)
    {
        VoucherRenewalPricing.LineAmountUah(liters, rate).Should().Be(expected);
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
            (50m, 2.5m),  // 125
            (20m, 3m),    // 60
            (10m, 1.05m)  // 11 (10.5 rounded away from zero, per line)
        };

        VoucherRenewalPricing.TotalUah(lines).Should().Be(196);
    }

    [Fact]
    public void TotalUah_ShouldBeZero_ForEmptyBatch()
    {
        VoucherRenewalPricing.TotalUah(Array.Empty<(decimal, decimal)>()).Should().Be(0);
    }
}
