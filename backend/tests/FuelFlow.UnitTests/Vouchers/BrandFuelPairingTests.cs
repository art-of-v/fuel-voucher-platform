using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// A voucher records its brand twice — printed on the paper as <c>provider</c>, and in the catalog as
/// the fuel type's <c>station_id</c>. Only the catalog side is constrained, so the two can disagree and
/// nothing in the schema would say so.
/// </summary>
public sealed class BrandFuelPairingTests
{
    [Fact]
    public void SameBrand_IsConsistent()
    {
        BrandFuelPairing.IsConsistent("okko", "okko").Should().BeTrue();
    }

    // Every real import hits this: the brand is read off the paper in caps, the catalog stores it lower.
    [Fact]
    public void CaseDoesNotMatter_BecauseOneSideComesOffPaper()
    {
        BrandFuelPairing.IsConsistent("OKKO", "okko").Should().BeTrue();
        BrandFuelPairing.IsConsistent("okko", "OKKO").Should().BeTrue();
    }

    [Theory]
    [InlineData(" OKKO ")]
    [InlineData("OKKO\n")]
    public void SurroundingWhitespaceIsIgnored(string printed)
    {
        BrandFuelPairing.IsConsistent(printed, "okko").Should().BeTrue();
    }

    // The case that motivates this: a KLO fuel stamped OKKO. Such a voucher matches no replacement on
    // renewal - both fields are compared together - and no fuel-type pricing describes it.
    [Fact]
    public void DifferentBrand_IsNotConsistent()
    {
        BrandFuelPairing.IsConsistent("OKKO", "klo").Should().BeFalse();
    }

    [Fact]
    public void NoCatalogBrand_LeavesTheVoucherToItsOwnMerits()
    {
        BrandFuelPairing.IsConsistent("OKKO", null).Should().BeTrue();
    }

    [Fact]
    public void NoBrandReadOffThePaper_IsNotReportedAsADisagreement()
    {
        BrandFuelPairing.IsConsistent(null, "okko").Should().BeTrue();
        BrandFuelPairing.IsConsistent("   ", "okko").Should().BeTrue();
    }
}