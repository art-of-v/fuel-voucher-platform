using FluentAssertions;
using FuelFlow.Features.Auth.Security;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// The one-time options preparation behind <see cref="OtpSecuritySetup.AddOtpSecurity"/>: settle
/// the effective pepper and hash the configured QA code with it (planning #682). Development keeps
/// the committed fallback, Production keeps the validated secret, and Staging - which may boot
/// without one - gets a fresh pepper for that process rather than silently using the public
/// Development value.
/// </summary>
public sealed class OtpSecuritySetupTests
{
    private const string StrongPepper = "Zt5m2K9pQr4sVw7xYb1cD6eF8gH3jL0nP2uA5iO7kM=";

    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    private static AuthOptions Build(string environment, Action<AuthOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<AuthOptions>();
        if (configure is not null)
            services.Configure(configure);
        services.AddOtpSecurity(Env(environment));

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AuthOptions>>().Value;
    }

    [Fact]
    public void Development_ShouldKeepTheFallback_AndHashTheQaCodeWithIt()
    {
        var options = Build("Development", o => o.QaTestAccessCode = "424242");

        options.OtpPepper.Should().Be(AuthOptions.DevelopmentOtpPepper);
        options.QaTestAccessCode.Should().BeNull("the plaintext is dropped after hashing");
        options.QaTestAccessCodeHash.Should()
            .Be(OtpCodeHasher.Hash("424242", AuthOptions.DevelopmentOtpPepper));
    }

    [Fact]
    public void Staging_WithoutAPepper_ShouldGenerateOneForThisProcess()
    {
        var options = Build("Staging");

        options.OtpPepper.Should().NotBe(AuthOptions.DevelopmentOtpPepper);
        Convert.FromBase64String(options.OtpPepper).Should().HaveCount(32,
            "the ephemeral pepper has the same 256 bits the environment variable would hold");
    }

    [Fact]
    public void Staging_WithAnEmptyValue_ShouldGenerateOneForThisProcess()
    {
        var options = Build("Staging", o => o.OtpPepper = "");

        options.OtpPepper.Should().NotBeNullOrWhiteSpace();
        options.OtpPepper.Should().NotBe(AuthOptions.DevelopmentOtpPepper);
    }

    [Fact]
    public void Development_WithAnEmptyValue_ShouldFallBackToTheDevelopmentPepper()
    {
        var options = Build("Development", o => o.OtpPepper = "");

        options.OtpPepper.Should().Be(AuthOptions.DevelopmentOtpPepper);
    }

    [Fact]
    public void Staging_ShouldNotReuseTheEphemeralPepperAcrossRestarts()
    {
        Build("Staging").OtpPepper.Should().NotBe(Build("Staging").OtpPepper);
    }

    [Fact]
    public void Production_ShouldKeepTheValidatedPepper()
    {
        var options = Build("Production", o => o.OtpPepper = StrongPepper);

        options.OtpPepper.Should().Be(StrongPepper);
    }

    [Fact]
    public void AnAbsentQaCode_ShouldLeaveTheHashUnset()
    {
        Build("Development").QaTestAccessCodeHash.Should().BeNull();
    }

    [Fact]
    public void TheQaCode_ShouldBeTrimmedBeforeHashing()
    {
        var options = Build("Development", o => o.QaTestAccessCode = "  424242  ");

        options.QaTestAccessCodeHash.Should()
            .Be(OtpCodeHasher.Hash("424242", AuthOptions.DevelopmentOtpPepper));
    }
}
