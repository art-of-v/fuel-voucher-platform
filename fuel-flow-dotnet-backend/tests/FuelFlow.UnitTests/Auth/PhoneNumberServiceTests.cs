using FluentAssertions;
using FuelFlow.Features.Auth.SharedServices;
using Xunit;

namespace FuelFlow.UnitTests.Auth;

public class PhoneNumberServiceTests
{
    private readonly PhoneNumberService _service;

    public PhoneNumberServiceTests()
    {
        _service = new PhoneNumberService();
    }

    [Theory]
    [InlineData("+12345678901", "+12345678901")]
    [InlineData("+380991234567", "+380991234567")]
    [InlineData("+44123456789", "+44123456789")]
    public void Normalize_ShouldReturnUnchanged_WhenPhoneNumberStartsWithPlus(string input, string expected)
    {
        var result = _service.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("2345678901", "+12345678901")]
    [InlineData("5551234567", "+15551234567")]
    [InlineData("9876543210", "+19876543210")]
    public void Normalize_ShouldAddPlusOne_WhenPhoneNumberIs10Digits(string input, string expected)
    {
        var result = _service.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("12345678901", "+12345678901")]
    [InlineData("15551234567", "+15551234567")]
    public void Normalize_ShouldAddPlus_WhenPhoneNumberIs11DigitsStartingWith1(string input, string expected)
    {
        var result = _service.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("(234) 567-8901", "+12345678901")]
    [InlineData("234-567-8901", "+12345678901")]
    [InlineData("234.567.8901", "+12345678901")]
    [InlineData("234 567 8901", "+12345678901")]
    [InlineData(" 234 567 8901 ", "+12345678901")]
    public void Normalize_ShouldRemoveFormatting_AndNormalize(string input, string expected)
    {
        var result = _service.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("+1 (234) 567-8901", "+12345678901")]
    [InlineData("+380 99 123 45 67", "+380991234567")]
    [InlineData("+44 123 456 789", "+44123456789")]
    public void Normalize_ShouldRemoveFormatting_WhenPhoneNumberStartsWithPlus(string input, string expected)
    {
        var result = _service.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("380991234567", "+380991234567")]
    [InlineData("441234567890", "+441234567890")]
    public void Normalize_ShouldAddPlus_WhenPhoneNumberIsLongerThan10DigitsAndDoesNotStartWithPlus(string input, string expected)
    {
        var result = _service.Normalize(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Normalize_ShouldThrowArgumentException_WhenPhoneNumberIsNullOrWhitespace(string? input)
    {
        var act = () => _service.Normalize(input);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Phone number cannot be empty*");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("abc")]
    [InlineData("1-2-3")]
    public void Normalize_ShouldThrowArgumentException_WhenPhoneNumberFormatIsInvalid(string input)
    {
        var act = () => _service.Normalize(input);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Invalid phone number format*");
    }

    [Fact]
    public void Normalize_ShouldHandleComplexFormatting()
    {
        var input = "+1 (555) 123-4567";
        var expected = "+15551234567";

        var result = _service.Normalize(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void Normalize_ShouldHandleInternationalFormat()
    {
        var input = "+380 (99) 123-45-67";
        var expected = "+380991234567";

        var result = _service.Normalize(input);

        result.Should().Be(expected);
    }
}
