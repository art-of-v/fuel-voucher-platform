using FluentAssertions;
using FuelFlow.Features.Vouchers.Renewal;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers.Renewal;

public sealed class VoucherRenewalTermTests
{
    [Fact]
    public void All_ShouldContainTheEightDistinctTiers()
    {
        VoucherRenewalTerms.All.Should().HaveCount(8).And.OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(VoucherRenewalTerm.OneWeek, "1w")]
    [InlineData(VoucherRenewalTerm.TwoWeeks, "2w")]
    [InlineData(VoucherRenewalTerm.OneMonth, "1m")]
    [InlineData(VoucherRenewalTerm.TwoMonths, "2m")]
    [InlineData(VoucherRenewalTerm.ThreeMonths, "3m")]
    [InlineData(VoucherRenewalTerm.FourMonths, "4m")]
    [InlineData(VoucherRenewalTerm.FiveMonths, "5m")]
    [InlineData(VoucherRenewalTerm.SixMonths, "6m")]
    public void Code_ShouldBeStable(VoucherRenewalTerm term, string expected)
    {
        term.Code().Should().Be(expected);
    }

    [Fact]
    public void TryFromCode_ShouldRoundTripEveryTier_CaseInsensitively()
    {
        foreach (var term in VoucherRenewalTerms.All)
        {
            VoucherRenewalTerms.TryFromCode(term.Code().ToUpperInvariant(), out var parsed).Should().BeTrue();
            parsed.Should().Be(term);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7d")]
    [InlineData("1y")]
    public void TryFromCode_ShouldReturnFalse_ForUnknownCode(string? code)
    {
        VoucherRenewalTerms.TryFromCode(code, out _).Should().BeFalse();
    }

    [Fact]
    public void ApplyTo_WeekTiers_ShouldAddCalendarDays()
    {
        var from = new DateOnly(2026, 1, 15);
        VoucherRenewalTerm.OneWeek.ApplyTo(from).Should().Be(new DateOnly(2026, 1, 22));
        VoucherRenewalTerm.TwoWeeks.ApplyTo(from).Should().Be(new DateOnly(2026, 1, 29));
    }

    [Fact]
    public void ApplyTo_MonthTiers_ShouldAddCalendarMonths()
    {
        var from = new DateOnly(2026, 1, 15);
        VoucherRenewalTerm.OneMonth.ApplyTo(from).Should().Be(new DateOnly(2026, 2, 15));
        VoucherRenewalTerm.SixMonths.ApplyTo(from).Should().Be(new DateOnly(2026, 7, 15));
    }

    [Fact]
    public void ApplyTo_MonthTier_ShouldClampShortMonths()
    {
        // Jan 31 + 1 month clamps to Feb 28 (2026 is not a leap year).
        VoucherRenewalTerm.OneMonth.ApplyTo(new DateOnly(2026, 1, 31)).Should().Be(new DateOnly(2026, 2, 28));
    }
}
