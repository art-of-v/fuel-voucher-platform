using FluentAssertions;
using FuelFlow.API.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// Covers the production-only startup guards, with the focus on the Monobank reconciliation guard:
/// a paid order's only backstop against a lost webhook must not be silently switched off. The guard
/// is acknowledge-to-disable, and — critically — an unset flag must default to on, never trip.
/// </summary>
public sealed class SecurityConfigurationValidatorTests
{
    // A Production configuration that passes every guard, so a single override isolates the rule
    // under test instead of tripping an unrelated guard first.
    private static Dictionary<string, string?> ValidProductionBaseline() => new()
    {
        ["Monobank:Enabled"] = "true",
        ["Monobank:PublicKey"] = "real-monobank-public-key",
        ["Monobank:ReconciliationEnabled"] = "true",
        ["SmsClub:Token"] = "sms-token",
        ["SmsClub:SenderName"] = "FuelFlow",
        ["DeviceAuth:Enabled"] = "true",
    };

    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static IHostEnvironment Env(string name = "Production")
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenProductionFullyConfigured()
    {
        var act = () => SecurityConfigurationValidator.Validate(Config(ValidProductionBaseline()), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Throws_WhenReconciliationDisabled_AndNotAcknowledged()
    {
        var settings = ValidProductionBaseline();
        settings["Monobank:ReconciliationEnabled"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ReconciliationEnabled is false*")
            .WithMessage("*AcknowledgeReconciliationDisabled*");
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenReconciliationDisabled_ButAcknowledged()
    {
        var settings = ValidProductionBaseline();
        settings["Monobank:ReconciliationEnabled"] = "false";
        settings["Monobank:AcknowledgeReconciliationDisabled"] = "true";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenReconciliationUnset_DefaultsOn()
    {
        // The regression that matters: absence of the key must read as enabled (default true),
        // so simply not configuring it never blocks startup.
        var settings = ValidProductionBaseline();
        settings.Remove("Monobank:ReconciliationEnabled");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenMonobankDisabled_EvenIfReconciliationOff()
    {
        // Reconciliation only matters while payments are enabled; with Monobank off the net is moot.
        var settings = ValidProductionBaseline();
        settings["Monobank:Enabled"] = "false";
        settings["Monobank:ReconciliationEnabled"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_OutsideProduction()
    {
        // Every guard is gated on Production; a misconfigured dev box must still boot.
        var settings = ValidProductionBaseline();
        settings["Monobank:ReconciliationEnabled"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Development"));
        act.Should().NotThrow();
    }
}
