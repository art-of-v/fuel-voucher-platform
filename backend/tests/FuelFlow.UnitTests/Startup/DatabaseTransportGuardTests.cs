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
/// The database transport guard: a production connection that does not verify the server's
/// certificate is a connection anyone on the network can read, and the old default made that the
/// easy thing to get.
/// </summary>
public sealed class DatabaseTransportGuardTests
{
    private static string ReadableMonobankKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(ecdsa.ExportSubjectPublicKeyInfoPem()));
    }

    /// <summary>
    /// A Production configuration that passes every other guard, so one override isolates the rule
    /// under test instead of tripping an unrelated guard first.
    /// </summary>
    private static Dictionary<string, string?> ValidProductionBaseline() => new()
    {
        ["Monobank:Enabled"] = "true",
        ["Monobank:PublicKey"] = ReadableMonobankKey(),
        ["Monobank:ReconciliationEnabled"] = "true",
        ["SmsClub:Token"] = "sms-token",
        ["SmsClub:SenderName"] = "FuelFlow",
        ["DeviceAuth:Enabled"] = "true",
        ["Database:ConnectionString"] =
            "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow;Password=secret;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt",
        // The schema migration has its own connection now, and it carries the money and the
        // personal data exactly like the traffic connection does - so it is held to the same
        // transport. The two roles are different on purpose: the traffic may not change the
        // schema, and the migration may not do the traffic's work.
        ["Database:MigrationConnectionString"] =
            "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_migrator;Password=secret;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt",
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
    public void Validate_DoesNotThrow_WhenTheConnectionVerifiesTheServerCertificate()
    {
        var act = () => SecurityConfigurationValidator.Validate(Config(ValidProductionBaseline()), Env());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Throws_WhenTheConnectionTrustsAnyCertificate()
    {
        var settings = ValidProductionBaseline();
        settings["Database:ConnectionString"] =
            "Host=postgres;Database=fuelflow;Username=fuelflow;Password=secret;SSL Mode=Require;Trust Server Certificate=true";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not verify the server's certificate*")
            .WithMessage("*VerifyFull*");
    }

    [Fact]
    public void Validate_Throws_WhenTheConnectionDisablesTls()
    {
        var settings = ValidProductionBaseline();
        settings["Database:ConnectionString"] =
            "Host=postgres;Database=fuelflow;Username=fuelflow;Password=secret;SSL Mode=Disable";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not verify the server's certificate*")
            .WithMessage("*VerifyFull*");
    }

    [Fact]
    public void Validate_Throws_WhenTheConnectionSaysNothingAboutTheTransport()
    {
        // A string with no SSL Mode is the dangerous one: the code would then append
        // "SSL Mode=Require;Trust Server Certificate=true" to it, which is the old default
        // wearing a different name.
        var settings = ValidProductionBaseline();
        settings["Database:ConnectionString"] =
            "Host=postgres;Database=fuelflow;Username=fuelflow;Password=secret";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not verify the server's certificate*");
    }

    [Fact]
    public void Validate_Throws_WhenTheConnectionOnlyEncrypts()
    {
        // "Require" encrypts the session against anyone - it does not check who is on the
        // other end, which is the half that matters.
        var settings = ValidProductionBaseline();
        settings["Database:ConnectionString"] =
            "Host=postgres;Database=fuelflow;Username=fuelflow;Password=secret;SSL Mode=Require";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not verify the server's certificate*");
    }

    [Fact]
    public void Validate_DoesNotThrow_WhenTheConnectionIsAbsent()
    {
        // An absent connection string is a different failure (the app cannot open a data source
        // at all), and this guard must not become the thing that complains about it: the tests
        // that do not care about the transport build a Production configuration without one.
        var settings = ValidProductionBaseline();
        settings.Remove("Database:ConnectionString");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_IgnoresTheTransportOutsideProduction()
    {
        // Staging runs on the same host and its own database, and its transport is a separate
        // decision. The guard is a Production rule; a development or staging configuration is
        // not its business.
        var settings = ValidProductionBaseline();
        settings["Database:ConnectionString"] =
            "Host=postgres;Database=fuelflow;Username=fuelflow;Password=secret;SSL Mode=Disable";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Staging"));

        act.Should().NotThrow();
    }
}
