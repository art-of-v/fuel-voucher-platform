using FluentAssertions;
using FuelFlow.Features.Ledger;
using Xunit;

namespace FuelFlow.UnitTests.Ledger;

public sealed class VatCalculatorTests
{
    [Fact]
    public void A_Price_Of_120_Carries_20_Of_Vat()
    {
        VatCalculator.VatFromGross(120m).Should().Be(20m);
        VatCalculator.NetFromGross(120m).Should().Be(100m);
    }

    [Fact]
    public void Vat_Is_A_Part_Of_The_Price_Not_A_Percentage_Added_To_It()
    {
        // The distinction that produces the classic first-entry error: 20% of 120 is 24, and 24 is
        // wrong. The VAT inside 120 at 20% is a sixth of it.
        VatCalculator.VatFromGross(120m).Should().NotBe(24m);
        VatCalculator.VatFromGross(120m).Should().Be(20m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(103.90)]
    [InlineData(1500.00)]
    [InlineData(92.90)]
    public void Net_Plus_Vat_Always_Reconstructs_The_Gross_To_The_Kopeck(decimal gross)
    {
        var (net, vat) = VatCalculator.Split(gross);

        (net + vat).Should().Be(gross,
            "a trial balance that is a kopeck out is a trial balance nobody trusts");
    }

    [Fact]
    public void A_Real_Pump_Price_Splits_Sensibly()
    {
        // 92.90 is a real offer price in the catalogue. At 20% the VAT is 15.48, not 18.58.
        var (net, vat) = VatCalculator.Split(92.90m);

        vat.Should().Be(15.48m);
        net.Should().Be(77.42m);
    }

    [Theory]
    [InlineData(120.05, 20.01)]
    [InlineData(0.05, 0.01)]
    public void Rounding_Is_Half_Away_From_Zero_Like_An_Invoice(decimal gross, decimal expectedVat)
    {
        VatCalculator.VatFromGross(gross).Should().Be(expectedVat);
    }

    [Fact]
    public void A_Rate_Other_Than_Twenty_Is_Respected()
    {
        // Fuel has carried a reduced rate in this jurisdiction before, so the rate is a parameter
        // rather than a constant baked into a ledger class.
        var (net, vat) = VatCalculator.Split(120m, 7m);

        vat.Should().Be(7.85m);
        net.Should().Be(112.15m);
    }

    [Fact]
    public void A_Zero_Rate_Yields_All_Net()
    {
        var (net, vat) = VatCalculator.Split(500m, 0m);

        vat.Should().Be(0m);
        net.Should().Be(500m);
    }

    [Fact]
    public void Zero_Is_Accepted_And_Not_Treated_As_A_Missing_Amount()
    {
        VatCalculator.Split(0m).Should().Be((0m, 0m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void A_Negative_Amount_Is_Refused(decimal gross)
    {
        // A negative gross is not a reversal - a reversal is a separate event with its own mapping.
        // Silently flipping the sign here would post a sale where a refund was meant.
        var act = () => VatCalculator.Split(gross);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_Negative_Rate_Is_Refused()
    {
        var act = () => VatCalculator.VatFromGross(100m, -1m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}