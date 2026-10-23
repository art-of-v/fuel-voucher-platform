using FluentAssertions;
using FuelFlow.Features.Auth.Security;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// The startup guard for the OTP pepper (planning #682): Production must not boot with a missing,
/// short, committed or placeholder pepper, because without it a leaked verification_codes table is
/// a million-guess search. Staging may boot without one - the app generates an ephemeral pepper for
/// that process - but a weak value that IS configured is still refused wherever it is configured.
/// </summary>
public sealed class OtpPepperValidatorTests
{
    private const string StrongPepper = "Zt5m2K9pQr4sVw7xYb1cD6eF8gH3jL0nP2uA5iO7kM=";

    private static IConfiguration Config(string? pepper) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:OtpPepper"] = pepper,
            })
            .Build();

    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    [Fact]
    public void Production_WithoutAPepper_ShouldRefuseToStart()
    {
        var act = () => OtpPepperValidator.Validate(Config(null), Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OtpPepper*")
            .WithMessage("*openssl rand -base64 32*");
    }

    [Fact]
    public void Production_WithTheDevelopmentFallback_ShouldRefuseToStart()
    {
        // The fallback is a committed string, so it is exactly as public as a placeholder.
        var act = () => OtpPepperValidator.Validate(Config(AuthOptions.DevelopmentOtpPepper), Env("Production"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*OtpPepper*");
    }

    [Fact]
    public void Production_WithAShortPepper_ShouldRefuseToStart()
    {
        var shortPepper = new string('a', OtpPepperValidator.MinimumLength - 1);
        var act = () => OtpPepperValidator.Validate(Config(shortPepper), Env("Production"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32*");
    }

    [Fact]
    public void Production_WithAPlaceholderPepper_ShouldRefuseToStart()
    {
        var act = () => OtpPepperValidator.Validate(
            Config("CHANGE_ME_otp_pepper_at_least_32_characters_long"), Env("Production"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*placeholder*");
    }

    [Fact]
    public void Production_WithAStrongPepper_ShouldStart()
    {
        var act = () => OtpPepperValidator.Validate(Config(StrongPepper), Env("Production"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Development_WithoutAPepper_ShouldStart()
    {
        var act = () => OtpPepperValidator.Validate(Config(null), Env("Development"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Staging_WithoutAPepper_ShouldStartOnTheEphemeralFallback()
    {
        var act = () => OtpPepperValidator.Validate(Config(null), Env("Staging"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Staging_WithAShortPepper_ShouldStillRefuseToStart()
    {
        var shortPepper = new string('a', OtpPepperValidator.MinimumLength - 1);
        var act = () => OtpPepperValidator.Validate(Config(shortPepper), Env("Staging"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32*");
    }
}
