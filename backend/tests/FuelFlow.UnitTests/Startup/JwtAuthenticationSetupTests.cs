using FluentAssertions;
using FuelFlow.API.Extensions;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// Covers the JWT signing configuration: where the key comes from, and which checks decide that a
/// token is one of ours.
/// </summary>
/// <remarks>
/// <para>
/// Two things used to sit here, and both are gone (planning #526, S-6). The first was a
/// committed fallback key - one constant, in the source, in every checkout, that Production would
/// accept if a configuration switch named <c>Testing</c> was set. The second was that same
/// switch, which also turned the issuer and audience checks off. Together they were a fail-open
/// JWT configuration one environment variable away from being live: a token minted for another
/// audience, or by anything else that knew the key, was accepted.
/// </para>
/// <para>
/// What replaced the constant is a key generated per process in Development, so there is nothing
/// in the repository to copy, leak, or share between machines. What replaced the switch is
/// nothing: the flag is not read any more, so setting it changes nothing at all.
/// </para>
/// </remarks>
public sealed class JwtAuthenticationSetupTests
{
    private const string ProductionSecret = "a-configured-production-secret-of-at-least-32-characters";

    /// <summary>
    /// The acceptance criterion of planning #526: a Production boot with <c>Testing=true</c> has to
    /// refuse. Before the fix it did not - the switch was enough to make Production accept the
    /// committed key from the source. Now the flag is not read, so the same configuration fails.
    /// </summary>
    [Fact]
    public void Production_RefusesToBoot_WithoutASecret_EvenWithTheTestingFlagSet()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Testing"] = "true",
            ["Jwt:Issuer"] = "FuelFlow",
            ["Jwt:Audience"] = "FuelFlow",
        });

        var act = () => Provider(config, Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Secret is not configured*");
    }

    /// <summary>
    /// The second half of the same fail-open: the switch used to turn the issuer and audience
    /// checks off, so a token that belonged to another audience was accepted here. There is no
    /// switch to turn them off with any more.
    /// </summary>
    [Fact]
    public void Production_ValidatesTheIssuerAndTheAudience_EvenWithTheTestingFlagSet()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Testing"] = "true",
            ["Jwt:Secret"] = ProductionSecret,
            ["Jwt:Issuer"] = "FuelFlow",
            ["Jwt:Audience"] = "FuelFlow",
        });

        using var provider = Provider(config, Env("Production"));
        var parameters = TokenValidationParameters(provider);

        parameters.ValidateIssuer.Should().BeTrue(
            "the issuer check is what says the token was minted by this platform");
        parameters.ValidateAudience.Should().BeTrue(
            "the audience check is what says the token was minted for this platform");
    }

    /// <summary>
    /// A development process with no configured key signs with one of its own, generated per
    /// start. Two processes therefore do not share a key, and nothing is committed.
    /// </summary>
    [Fact]
    public void Development_GeneratesItsOwnSigningKey_WhenNoSecretIsConfigured()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "FuelFlow",
            ["Jwt:Audience"] = "FuelFlow",
        });

        using var first = Provider(config, Env("Development"));
        using var second = Provider(config, Env("Development"));

        var firstSecret = SigningKey(first);
        var secondSecret = SigningKey(second);

        firstSecret.Should().NotBeNullOrWhiteSpace();
        firstSecret.Length.Should().BeGreaterThanOrEqualTo(32);
        firstSecret.Should().NotBe(secondSecret,
            "a generated key is per process: two machines must not be able to forge a token for "
            + "each other, and no key may live in the repository");
    }

    /// <summary>
    /// The flag is not read any more, so setting it in Development changes nothing - and what it
    /// used to change (a shared key, and no issuer or audience checks) is gone with it.
    /// </summary>
    [Fact]
    public void Development_IgnoresTheTestingFlag()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Testing"] = "true",
            ["Jwt:Issuer"] = "FuelFlow",
            ["Jwt:Audience"] = "FuelFlow",
        });

        using var provider = Provider(config, Env("Development"));
        var parameters = TokenValidationParameters(provider);

        parameters.ValidateIssuer.Should().BeTrue();
        parameters.ValidateAudience.Should().BeTrue();
    }

    /// <summary>
    /// The configured key is the key that signs. Without this the refusal above could pass for
    /// the wrong reason - a boot that fails for every reason is not a boot that fails for the
    /// right one.
    /// </summary>
    [Fact]
    public void Production_UsesTheConfiguredSecret()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = ProductionSecret,
            ["Jwt:Issuer"] = "FuelFlow",
            ["Jwt:Audience"] = "FuelFlow",
        });

        using var provider = Provider(config, Env("Production"));

        SigningKey(provider).Should().Be(ProductionSecret);
    }

    /// <summary>
    /// A key that is too short is refused in every environment, generated or configured. The
    /// length is the floor the signing algorithm needs, not a policy that only production gets.
    /// </summary>
    [Fact]
    public void A_ConfiguredSecretShorterThan32Characters_IsRefused()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "too-short",
            ["Jwt:Issuer"] = "FuelFlow",
            ["Jwt:Audience"] = "FuelFlow",
        });

        var act = () => Provider(config, Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 32 characters*");
    }

    /// <summary>
    /// The placeholder that ships in the example files is not a key either: it is refused the
    /// same way a missing one is.
    /// </summary>
    [Fact]
    public void Production_RefusesThePlaceholderSecret()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "CHANGE_THIS_TO_A_SECURE_RANDOM_KEY_IN_PRODUCTION_AT_LEAST_32_CHARACTERS",
            ["Jwt:Issuer"] = "FuelFlow",
            ["Jwt:Audience"] = "FuelFlow",
        });

        var act = () => Provider(config, Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Secret is not configured*");
    }

    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static IWebHostEnvironment Env(string name)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    private static ServiceProvider Provider(IConfiguration config, IWebHostEnvironment environment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJwtAuth(config, environment);
        return services.BuildServiceProvider();
    }

    private static string SigningKey(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<JwtOptions>>().Value.Secret;

    private static TokenValidationParameters TokenValidationParameters(IServiceProvider provider)
    {
        var monitor = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        return monitor.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters!;
    }
}
