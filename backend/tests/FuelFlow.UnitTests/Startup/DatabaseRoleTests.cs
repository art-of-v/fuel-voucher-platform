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
/// The application's database role, and the guards that keep it an ordinary login.
/// </summary>
/// <remarks>
/// <para>
/// One PostgreSQL role used to serve both the application's traffic and the schema
/// migrations, and that role was the cluster's SUPERUSER - the one role the postgres
/// image creates. The consequence of any future defect that reaches the database was
/// therefore not "an API bug leaks a table" but "an API bug owns the cluster": read every
/// role's password hash out of <c>pg_authid</c>, create a new superuser, drop the
/// database, or write straight into the audit trail the money reconciliation depends on.
/// </para>
/// <para>
/// Two guards hold the separation in place, and they are different kinds of guard on
/// purpose. The configuration guard reads what the operator WROTE (the two connection
/// strings must not name the same role); the catalogue guard asks the database what the
/// connected role IS (a role called <c>fuelflow_app</c> that happens to be a superuser is
/// refused exactly like a role called <c>postgres</c>). Neither of them can replace the
/// other: a name can lie, and a catalogue cannot be read before the connection is open.
/// </para>
/// </remarks>
public sealed class DatabaseRoleTests
{
    private static string ReadableMonobankKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(ecdsa.ExportSubjectPublicKeyInfoPem()));
    }

    /// <summary>
    /// A Production configuration that passes every guard, so one override isolates the
    /// rule under test instead of tripping an unrelated guard first.
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
            "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_app;Password=secret;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt",
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

    // ---------------------------------------------------------------------------------
    // The catalogue guard: what the connected role is allowed to be.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void An_Ordinary_Login_Is_Allowed()
    {
        var role = new DatabaseRoleValidator.RoleAttributes(
            "fuelflow_app", IsSuperuser: false, CanCreateRole: false,
            CanCreateDatabase: false, BypassesRowSecurity: false);

        DatabaseRoleValidator.RefusalReason(role).Should().BeNull();
    }

    [Fact]
    public void A_Superuser_Is_Refused_And_The_Role_Is_Named()
    {
        var role = new DatabaseRoleValidator.RoleAttributes(
            "postgres", IsSuperuser: true, CanCreateRole: false,
            CanCreateDatabase: false, BypassesRowSecurity: false);

        var reason = DatabaseRoleValidator.RefusalReason(role);

        reason.Should().NotBeNull();
        reason!.Should().Contain("postgres");
        reason.Should().Contain("SUPERUSER");
    }

    [Fact]
    public void A_Role_That_May_Create_Roles_Is_Refused()
    {
        // A role that may create a role can create itself a superuser: that is the same
        // escalation as being one, one step removed.
        var role = new DatabaseRoleValidator.RoleAttributes(
            "fuelflow_app", IsSuperuser: false, CanCreateRole: true,
            CanCreateDatabase: false, BypassesRowSecurity: false);

        var reason = DatabaseRoleValidator.RefusalReason(role);

        reason.Should().NotBeNull();
        reason!.Should().Contain("CREATE ROLE");
    }

    [Fact]
    public void A_Role_That_May_Create_Databases_Is_Refused()
    {
        // Another database on the same cluster is somebody else's data.
        var role = new DatabaseRoleValidator.RoleAttributes(
            "fuelflow_app", IsSuperuser: false, CanCreateRole: false,
            CanCreateDatabase: true, BypassesRowSecurity: false);

        var reason = DatabaseRoleValidator.RefusalReason(role);

        reason.Should().NotBeNull();
        reason!.Should().Contain("CREATE DATABASE");
    }

    [Fact]
    public void A_Role_That_Bypasses_Row_Level_Security_Is_Refused()
    {
        var role = new DatabaseRoleValidator.RoleAttributes(
            "fuelflow_app", IsSuperuser: false, CanCreateRole: false,
            CanCreateDatabase: false, BypassesRowSecurity: true);

        var reason = DatabaseRoleValidator.RefusalReason(role);

        reason.Should().NotBeNull();
        reason!.Should().Contain("ROW LEVEL SECURITY");
    }

    // ---------------------------------------------------------------------------------
    // The configuration guard: the traffic and the migration must not share a role.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Validate_DoesNotThrow_WhenTheTwo_Roles_Are_Different()
    {
        var act = () => SecurityConfigurationValidator.Validate(
            Config(ValidProductionBaseline()), Env());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Throws_WhenTheTraffic_And_The_Migration_Share_A_Role()
    {
        // This is the configuration that puts the whole finding back: one role for the
        // traffic and for the schema, and a superuser at that.
        var settings = ValidProductionBaseline();
        settings["Database:MigrationConnectionString"] =
            "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_app;Password=secret;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*both connect as the role*")
            .WithMessage("*fuelflow_app*");
    }

    [Fact]
    public void Validate_Throws_WhenThe_Migration_Has_No_Connection_String()
    {
        // Without a second connection string the application would migrate the schema
        // through the very connection its traffic runs on, which is the role that must
        // not be able to change the schema.
        var settings = ValidProductionBaseline();
        settings.Remove("Database:MigrationConnectionString");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Database:MigrationConnectionString is not set*");
    }

    [Fact]
    public void Validate_Throws_When_The_Migration_Connection_Does_Not_Verify_The_Server()
    {
        // The migration connection carries the money and the personal data exactly like
        // the traffic connection does: a migration may read the rows a backfill rewrites.
        var settings = ValidProductionBaseline();
        settings["Database:MigrationConnectionString"] =
            "Host=postgres;Database=fuelflow;Username=fuelflow_migrator;Password=secret;SSL Mode=Require;Trust Server Certificate=true";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not verify the server's certificate*");
    }

    [Fact]
    public void Validate_Ignores_The_Roles_Outside_Production()
    {
        // Staging is a disposable box behind an HTTP Basic credential and it connects as
        // its own cluster's superuser on purpose. The guard is a Production rule.
        var settings = ValidProductionBaseline();
        settings["Database:MigrationConnectionString"] =
            "Host=postgres;Database=fuelflow;Username=fuelflow_app;Password=secret;SSL Mode=Disable";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Staging"));

        act.Should().NotThrow();
    }
}
