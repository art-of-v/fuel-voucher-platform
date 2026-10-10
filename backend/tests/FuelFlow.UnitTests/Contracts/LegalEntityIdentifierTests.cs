using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Contracts;

/// <summary>
/// The legal-entity identifier formats, as observed in #128: a company was created with EDRPOU
/// <c>1234567890456565</c> - 16 digits, accepted because the only check was "not empty". These pin
/// the format gate that now runs on every write path.
/// </summary>
public sealed class LegalEntityIdentifierTests
{
    // The value from the issue, verbatim.
    private const string ObservedGarbage = "1234567890456565";

    [Theory]
    [InlineData("3852814")]           // one short
    [InlineData("385281470")]         // one long
    public void Rejects_Edrpou_ThatIsNotEightDigits(string edrpou)
        => LegalEntityIdentifier.DescribeEdrpouProblem(edrpou).Should().NotBeNull();

    [Theory]
    [InlineData(ObservedGarbage)]    // 16 digits - the defect in #128
    [InlineData("3852814 ")]
    [InlineData("3852814a")]          // a letter in the last position
    [InlineData("38528 47")]          // a space inside the number
    [InlineData("385-28147")]
    [InlineData("3.8528147")]
    [InlineData("abcdefgh")]
    [InlineData("３８５２８１４７")]      // fullwidth digits: 8 characters, not 8 ASCII digits
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_Malformed_Edrpou(string? edrpou)
        => LegalEntityIdentifier.DescribeEdrpouProblem(edrpou).Should().NotBeNull();

    [Theory]
    [InlineData("38528147")]
    [InlineData("00000000")]
    [InlineData(" 38528147 ")]        // surrounding whitespace is what a paste adds; tolerate it
    public void Accepts_EightDigit_Edrpou(string edrpou)
        => LegalEntityIdentifier.DescribeEdrpouProblem(edrpou).Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Accepts_Absent_Vat_Because_Vat_Is_Optional(string? vat)
        => LegalEntityIdentifier.DescribeVatProblem(vat).Should().BeNull();

    [Theory]
    [InlineData("123456789012")]      // 12 digits
    [InlineData(" 123456789012 ")]
    public void Accepts_TwelveDigit_Vat(string vat)
        => LegalEntityIdentifier.DescribeVatProblem(vat).Should().BeNull();

    [Theory]
    [InlineData("12345678901")]       // 11 digits
    [InlineData("1234567890123")]     // 13 digits
    [InlineData("12345678901x")]
    [InlineData("not-a-vat")]
    public void Rejects_Present_But_Malformed_Vat(string vat)
    {
        // A present-but-wrong VAT is reported rather than ignored: the operator who typed it should
        // hear that it is wrong instead of having it quietly dropped.
        LegalEntityIdentifier.DescribeVatProblem(vat).Should().NotBeNull();
    }

    [Fact]
    public void ErrorMessages_Say_TheExpectedLength()
    {
        // The operator has to be able to fix the field from the message alone.
        LegalEntityIdentifier.DescribeEdrpouProblem(ObservedGarbage)
            .Should().Be($"EDRPOU must be exactly {LegalEntityIdentifier.EdrpouLength} digits");

        LegalEntityIdentifier.DescribeVatProblem("123")
            .Should().Be($"VAT must be exactly {LegalEntityIdentifier.VatLength} digits");
    }
}