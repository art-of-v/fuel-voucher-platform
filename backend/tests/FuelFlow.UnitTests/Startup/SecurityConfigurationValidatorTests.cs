using System.Security.Cryptography;
using System.Text;
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
    /// <summary>
    /// A key in the shape production actually holds: the base64-of-PEM that Monobank's
    /// <c>GET /api/merchant/pubkey</c> returns. The baseline has to be a real one, because the
    /// startup guard now refuses a value it cannot read — which is the production incident it exists
    /// for, and a placeholder string would trip it for the wrong reason.
    /// </summary>
    private static string ReadableMonobankKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(ecdsa.ExportSubjectPublicKeyInfoPem()));
    }

    // A Production configuration that passes every guard, so a single override isolates the rule
    // under test instead of tripping an unrelated guard first.
    private static Dictionary<string, string?> ValidProductionBaseline() => new()
    {
        ["Monobank:Enabled"] = "true",
        ["Monobank:PublicKey"] = ReadableMonobankKey(),
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

    [Fact]
    public void Validate_Throws_WhenPublicKeyIsTruncated()
    {
        // The production case, verbatim: Monobank returns 240 characters, the env file held 100 of
        // them. Every "is it set?" check passed, and then every webhook died inside signature
        // verification — so no payment was ever confirmed by its own callback and reconciliation
        // silently became the only thing completing orders.
        var settings = ValidProductionBaseline();
        var full = settings["Monobank:PublicKey"]!;
        settings["Monobank:PublicKey"] = full[..100];

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a readable public key*")
            .WithMessage("*truncated*");
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenPublicKeyIsAReadablePem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = ValidProductionBaseline();
        settings["Monobank:PublicKey"] = ecdsa.ExportSubjectPublicKeyInfoPem();

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenSandboxTokenSet_WithoutASecondKey()
    {
        // Monobank signs both merchants with one company-wide key, so the sandbox needs no key of its
        // own. Requiring one would mean a second copy of a value that can only ever be wrong.
        var settings = ValidProductionBaseline();
        settings["Monobank:SandboxToken"] = "sandbox-token";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenBothMerchantsFullyConfigured()
    {
        var settings = ValidProductionBaseline();
        settings["Monobank:SandboxToken"] = "sandbox-token";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenNoSandboxConfiguredAtAll()
    {
        // One merchant is a valid configuration: QA then simply has no sandbox to route to, which
        // the resolver refuses loudly at checkout rather than at boot.
        var settings = ValidProductionBaseline();
        settings.Remove("Monobank:SandboxToken");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Throws_WhenVoucherExpirationDisabled_AndNotAcknowledged()
    {
        // Selling expired stock is the failure this blocks: fulfilment hands the buyer the
        // most-expired voucher it has, and only the mobile card notices afterwards.
        var settings = ValidProductionBaseline();
        settings["VoucherExpiration:Enabled"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*VoucherExpiration:Enabled is false*")
            .WithMessage("*AcknowledgeDisabledInProduction*");
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenVoucherExpirationDisabled_ButAcknowledged()
    {
        // The sandbox really does test against expired stock today, so the state must be
        // expressible - as a decision that was written down, not as a default nobody re-reads.
        var settings = ValidProductionBaseline();
        settings["VoucherExpiration:Enabled"] = "false";
        settings["VoucherExpiration:AcknowledgeDisabledInProduction"] = "true";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenVoucherExpirationUnset_DefaultsOn()
    {
        // Same reasoning as the reconciliation guard: absence must read as enabled, so an
        // environment that never mentions the key can never trip this.
        var settings = ValidProductionBaseline();
        settings.Remove("VoucherExpiration:Enabled");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenMonobankDisabled_EvenIfVoucherExpirationOff()
    {
        // The gate only endangers a paying customer while payments are live.
        var settings = ValidProductionBaseline();
        settings["Monobank:Enabled"] = "false";
        settings["VoucherExpiration:Enabled"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_OutsideProduction_WithVoucherExpirationOff()
    {
        var settings = ValidProductionBaseline();
        settings["VoucherExpiration:Enabled"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Development"));
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Throws_WhenMonobankUsesATestToken_AndNotAcknowledged()
    {
        // The failure this blocks is not an error, it is a launch that looks successful and takes
        // no money: invoices settle in Monobank's sandbox while orders are created and marked paid.
        var settings = ValidProductionBaseline();
        settings["Monobank:Token"] = "test_abc123";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TEST merchant token*")
            .WithMessage("*AcknowledgeTestTokenInProduction*");
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenTestTokenAcknowledged()
    {
        // The sandbox phase is deliberate today, so the state must be expressible - as a decision
        // that was written down.
        var settings = ValidProductionBaseline();
        settings["Monobank:Token"] = "test_abc123";
        settings["Monobank:AcknowledgeTestTokenInProduction"] = "true";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenMonobankTokenIsLive()
    {
        var settings = ValidProductionBaseline();
        settings["Monobank:Token"] = "live_abc123";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    // A token with no prefix at all is somebody's live token as far as this rule can tell - the
    // check is for the recognisable sandbox prefix, not a claim to verify arbitrary tokens.
    [Fact]
    public void Validate_DoesNotThrow_WhenMonobankTokenIsUnprefixed()
    {
        var settings = ValidProductionBaseline();
        settings["Monobank:Token"] = "abc123";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenMonobankDisabled_EvenOnATestToken()
    {
        var settings = ValidProductionBaseline();
        settings["Monobank:Enabled"] = "false";
        settings["Monobank:Token"] = "test_abc123";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    // --- the staff sign-in's second factor --------------------------------

    [Fact]
    public void Validate_DoesNotThrow_WhenTheSecondFactorIsUnset()
    {
        // Absence of the key must read as required (the default is true), so simply not
        // configuring it never blocks startup - and never turns the factor off either.
        var settings = ValidProductionBaseline();
        settings.Remove("Auth:StaffSecondFactorRequired");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Throws_WhenTheSecondFactorIsOff()
    {
        // A staff sign-in that completes with a code from a mailbox, for an account that can move
        // money, is not a control. Production refuses to start in this state.
        var settings = ValidProductionBaseline();
        settings["Auth:StaffSecondFactorRequired"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*StaffSecondFactorRequired is false*");
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenTheSecondFactorIsOffOutsideProduction()
    {
        // The local development loop and a rollback need to be expressible.
        var settings = ValidProductionBaseline();
        settings["Auth:StaffSecondFactorRequired"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Staging"));
        act.Should().NotThrow();
    }

    // --- the email break-glass -------------------------------------------

    [Fact]
    public void Validate_Throws_WhenTheEmailBreakGlassIsOn_AndNotAcknowledged()
    {
        // The channel that delivers the staff code to a mailbox has to be a decision, not a
        // variable somebody flipped during an outage and forgot.
        var settings = ValidProductionBaseline();
        settings["Auth:AdminOtpViaEmail"] = "true";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AdminOtpViaEmail is true*")
            .WithMessage("*AdminOtpViaEmailBreakGlassAcknowledged*");
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenTheEmailBreakGlassIsAcknowledged()
    {
        var settings = ValidProductionBaseline();
        settings["Auth:AdminOtpViaEmail"] = "true";
        settings["Auth:AdminOtpViaEmailBreakGlassAcknowledged"] = "true";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenTheEmailBreakGlassIsOff()
    {
        // The default: the staff code goes to the SIM.
        var settings = ValidProductionBaseline();
        settings["Auth:AdminOtpViaEmail"] = "false";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());
        act.Should().NotThrow();
    }
}
