using FluentAssertions;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.UnitTests.Observability;

public sealed class ObservabilitySecurityTests
{
    [Theory]
    [InlineData("+380991234567", "****4567")]
    [InlineData("12345", "****2345")]
    [InlineData("1234", "****")]
    [InlineData("", "****")]
    [InlineData(null, "****")]
    public void MaskPhoneNumber_HidesAllButLastFourDigits(string? input, string expected)
    {
        SensitiveDataRedactor.MaskPhoneNumber(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("fuelflow-api", null)]
    [InlineData("", "secret")]
    [InlineData("fuelflow-api", "")]
    public void ValidateCredentials_RejectsIncompleteEnabledCredentials(string? username, string? password)
    {
        var options = new ObservabilityOptions.LokiOptions
        {
            Enabled = true,
            Username = username,
            Password = password
        };

        var act = options.ValidateCredentials;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Username and Password are required*");
    }

    [Fact]
    public void ValidateCredentials_AcceptsCompleteEnabledCredentials()
    {
        var options = new ObservabilityOptions.LokiOptions
        {
            Enabled = true,
            Username = "fuelflow-api",
            Password = "test-secret"
        };

        options.Invoking(value => value.ValidateCredentials()).Should().NotThrow();
    }

    [Fact]
    public void ValidateCredentials_AllowsDisabledSinkWithoutCredentials()
    {
        new ObservabilityOptions.LokiOptions()
            .Invoking(value => value.ValidateCredentials())
            .Should().NotThrow();
    }
}
