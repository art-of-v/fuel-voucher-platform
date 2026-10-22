using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using FuelFlow.API.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// A secret that is still the placeholder from the env template is a secret in a public repository,
/// and it must not reach Production or Staging.
/// </summary>
/// <remarks>
/// <para>
/// The finding this answers is planning #620 (S-11). The template
/// <c>deploy/.env.production.example</c> ships four placeholder secrets, and the application
/// accepted every one of them: the JWT signing key (53 characters, so it passed the length check),
/// the voucher QR cipher key, the database password and the Redis password. The guard that was
/// supposed to catch them compared the value against a DIFFERENT literal - one no template has
/// ever shipped - so the API booted, silently, on the published value.
/// </para>
/// <para>
/// The repository is public until 2026-11-01, so that value is on the internet. An HS256 access
/// token signed with it can be forged by anybody: full staff console access - refunds, payment
/// placement, customer PII, the redeemable voucher payloads - with the whole session model
/// (rotation, reuse detection, device binding) bypassed, because the forger never holds a refresh
/// token.
/// </para>
/// <para>
/// The tests read the values out of the templates themselves rather than repeating them here. A
/// test that repeats the value proves that the value is refused; a test that READS the value proves
/// that what the template ships is what the guard refuses - and it fails the moment somebody edits
/// the template to a wording the guard does not know, which is exactly how the old literal check
/// went stale.
/// </para>
/// </remarks>
public sealed class PlaceholderSecretGuardTests
{
    /// <summary>
    /// The production env template.
    /// </summary>
    private const string ProductionTemplate = "deploy/.env.production.example";

    /// <summary>
    /// The variables the env template ships a placeholder for, and the setting each one reaches.
    /// </summary>
    /// <remarks>
    /// The database has more than one password since the three-role layout (app PR #1492): the
    /// superuser's, the traffic role's and the migrator's. The list therefore names the
    /// VARIABLES rather than the roles, and a variable the template does not carry is skipped -
    /// so the test follows the template instead of going stale with it, and a template that
    /// changes which password the application opens its connection with cannot leave a hole here.
    /// </remarks>
    private static readonly (string Variable, string Setting)[] GuardedVariables =
    [
        ("JWT_SECRET", "Jwt:Secret"),
        ("QR_CIPHER_KEY", "QrCipher:Key"),
        ("POSTGRES_PASSWORD", "Database:ConnectionString"),
        ("POSTGRES_APP_PASSWORD", "Database:ConnectionString"),
        ("POSTGRES_MIGRATOR_PASSWORD", "Database:ConnectionString"),
        ("REDIS_PASSWORD", "ConnectionStrings:Redis"),
    ];

    /// <summary>
    /// A Production configuration that passes every other guard, so one override isolates the rule
    /// under test instead of tripping an unrelated guard first.
    /// </summary>
    private static Dictionary<string, string?> ValidProductionBaseline() => new()
    {
        ["Jwt:Secret"] = "a-configured-jwt-secret-of-at-least-32-characters",
        ["QrCipher:Key"] = Convert.ToBase64String(RandomKeyBytes()),
        ["Database:ConnectionString"] =
            "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_app;"
            + "Password=dummy-database-password;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt",
        // The schema migration has its own connection, and it is a role of its own: the
        // application's traffic may not change the schema, and the migration may not do the
        // traffic's work. A Production configuration that passes every guard has both.
        ["Database:MigrationConnectionString"] =
            "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_migrator;"
            + "Password=dummy-migration-password;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt",
        ["ConnectionStrings:Redis"] = "redis:6379,password=dummy-redis-password",
        ["Monobank:Enabled"] = "true",
        ["Monobank:PublicKey"] = ReadableMonobankKey(),
        ["Monobank:ReconciliationEnabled"] = "true",
        ["SmsClub:Token"] = "sms-token",
        ["SmsClub:SenderName"] = "FuelFlow",
        ["DeviceAuth:Enabled"] = "true",
    };

    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    private static IWebHostEnvironment WebEnv(string name)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    [Fact]
    public void Every_Secret_The_Template_Ships_Is_Refused_In_Production()
    {
        var covered = 0;

        foreach (var (variable, _) in GuardedVariables)
        {
            var value = TemplateValueOrNull(ProductionTemplate, variable);
            if (value is null) continue;

            covered++;
            var settings = ValidProductionBaseline();
            ApplyTemplateValue(settings, variable, value);

            var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Production"));

            act.Should().Throw<InvalidOperationException>(
                $"{variable} is still the placeholder the template ships, and Production booted on it");
        }

        covered.Should().BeGreaterThanOrEqualTo(3,
            "the template ships the JWT key, the QR cipher key, the database password and the Redis "
            + "password - and the guard has to cover the ones it ships, or the test proves nothing");
    }

    [Fact]
    public void Every_Secret_The_Template_Ships_Is_Refused_In_Staging()
    {
        // Staging is a second stack on the same host, deployed from the same kind of template. A
        // placeholder that is refused in Production and accepted in Staging is one that gets
        // copied into Production by the next person who reads the wrong file.
        foreach (var (variable, _) in GuardedVariables)
        {
            var value = TemplateValueOrNull(ProductionTemplate, variable);
            if (value is null) continue;

            var settings = ValidProductionBaseline();
            ApplyTemplateValue(settings, variable, value);

            var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Staging"));

            act.Should().Throw<InvalidOperationException>(
                $"{variable} is still the placeholder the template ships, and Staging booted on it");
        }
    }

    [Fact]
    public void The_Template_Still_Ships_The_Jwt_Key_As_A_Placeholder()
    {
        // If the template stopped shipping a placeholder for the JWT key, this file's premise
        // would be a stale document - and the guard would be guarding nothing.
        TemplateValue(ProductionTemplate, "JWT_SECRET").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_Refusal_Names_The_Setting_That_Has_To_Be_Fixed()
    {
        // An exception that says "a secret is a placeholder" sends the operator to the env file
        // with nothing to look for. The setting is named instead.
        var settings = ValidProductionBaseline();
        settings["Jwt:Secret"] = TemplateValue("deploy/.env.production.example", "JWT_SECRET");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Secret*")
            .WithMessage("*placeholder*");
    }

    [Fact]
    public void The_Database_Password_Is_Refused_Wherever_It_Arrives()
    {
        // The database password is not a setting of its own: it reaches the application inside the
        // connection string, so that is where it is read - and that is enough, because a
        // placeholder password that reaches the connection string is a placeholder password that is
        // live.
        var settings = ValidProductionBaseline();
        settings["Database:ConnectionString"] =
            "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_app;Password="
            + TemplateValue("deploy/.env.production.example", "POSTGRES_PASSWORD")
            + ";SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Database:ConnectionString*");
    }

    [Fact]
    public void The_Migration_Password_Is_Refused_Wherever_It_Arrives()
    {
        // The schema migration has its own role since the three-role layout, and its own password.
        // A role that can change the schema is a role that can change the data in it, so its
        // password is a secret of the same class as the traffic role's.
        var settings = ValidProductionBaseline();
        settings["Database:MigrationConnectionString"] =
            "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_migrator;Password="
            + TemplateValue("deploy/.env.production.example", "POSTGRES_PASSWORD")
            + ";SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt";

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Database:MigrationConnectionString*");
    }

    [Fact]
    public void The_Redis_Password_Is_Refused_Wherever_It_Arrives()
    {
        var settings = ValidProductionBaseline();
        settings["ConnectionStrings:Redis"] =
            "redis:6379,password=" + TemplateValue("deploy/.env.production.example", "REDIS_PASSWORD");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Redis*");
    }

    [Fact]
    public void A_Real_Secret_Is_Accepted()
    {
        // The other half of the criterion: a guard that refuses everything is not a guard, it is an
        // outage. The baseline holds real values and must pass.
        var act = () => SecurityConfigurationValidator.Validate(Config(ValidProductionBaseline()), Env("Production"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Development_Is_Exempt()
    {
        // A developer's box runs on generated keys and a fake SMS provider. A guard that stopped it
        // from starting would be a guard that would be turned off.
        var settings = ValidProductionBaseline();
        settings["Jwt:Secret"] = TemplateValue("deploy/.env.production.example", "JWT_SECRET");

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Development"));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("CHANGE_ME_at_least_32_characters_long_random_string")]
    [InlineData("changeme")]
    [InlineData("change-me")]
    [InlineData("CHANGE_ME")]
    [InlineData("your-secret-here")]
    [InlineData("your_secret")]
    [InlineData("PLACEHOLDER")]
    [InlineData("placeholder")]
    [InlineData("example-secret")]
    [InlineData("replace_me")]
    [InlineData("insert_here")]
    public void A_Secret_That_Reads_Like_A_Placeholder_Is_Refused(string value)
    {
        // The old guard compared the value against ONE literal, so a template that spelled its
        // placeholder differently - or an operator who typed their own - sailed past it. The rule is
        // the pattern now, and every wording of it is refused.
        var settings = ValidProductionBaseline();
        settings["Jwt:Secret"] = value;

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Secret*");
    }

    [Theory]
    [InlineData("kJ8vQ2mZpL4xN7tR9wY3bF6hD1sG5cA0eU2iO7nP8qV4xZ6")]
    [InlineData("3f8a1c9d2e4b6f0a7c5e3d1b9f7a5c3e1d9b7f5a3c1e9d7b5f3a1c9e7d5b3f1a")]
    [InlineData("9c4f2a8e6d0b5f3a7c9e1d3b5f7a9c2e4d6b8f0a2c4e6d8b1f3a5c7e9d2b4f6a8c0")]
    public void A_Secret_That_Carries_No_Placeholder_Wording_Is_Accepted(string value)
    {
        // The false-positive half. A random secret cannot contain the wordings by accident - they
        // are long enough and specific enough for that - but a gate that cries wolf is a gate
        // people learn to skip.
        var settings = ValidProductionBaseline();
        settings["Jwt:Secret"] = value;

        var act = () => SecurityConfigurationValidator.Validate(Config(settings), Env("Production"));

        act.Should().NotThrow();
    }

    [Fact]
    public void The_Authentication_Setup_Refuses_The_Template_Placeholder_Too()
    {
        // The validator is not the only place the value is refused, and the one that runs FIRST is
        // the JWT setup: the template's value is 53 characters long, so it passed the length check
        // and reached the signer. This is the check that stops it there.
        var config = Config(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = TemplateValue("deploy/.env.production.example", "JWT_SECRET"),
            ["Jwt:Issuer"] = "FuelFlow",
            ["Jwt:Audience"] = "FuelFlow",
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddJwtAuth(config, WebEnv("Production"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Secret*");
    }

    /// <summary>
    /// Puts the template's own value into the setting the variable is deployed through.
    /// </summary>
    private static void ApplyTemplateValue(Dictionary<string, string?> settings, string variable, string value)
    {
        switch (variable)
        {
            case "JWT_SECRET":
                settings["Jwt:Secret"] = value;
                break;
            case "QR_CIPHER_KEY":
                settings["QrCipher:Key"] = value;
                break;
            case "POSTGRES_PASSWORD":
            case "POSTGRES_APP_PASSWORD":
                // Whichever role's password the compose file puts in the connection string is
                // the password the application opens the database with, and it is the same
                // connection string the guard reads.
                settings["Database:ConnectionString"] =
                    "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_app;Password="
                    + value + ";SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt";
                break;
            case "POSTGRES_MIGRATOR_PASSWORD":
                settings["Database:MigrationConnectionString"] =
                    "Host=postgres;Port=5432;Database=fuelflow;Username=fuelflow_migrator;Password="
                    + value + ";SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt";
                break;
            case "REDIS_PASSWORD":
                settings["ConnectionStrings:Redis"] = "redis:6379,password=" + value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variable), variable, "unknown variable");
        }
    }

    /// <summary>
    /// The value the env template ships for a variable, or null when the template does not carry
    /// that variable.
    /// </summary>
    private static string? TemplateValueOrNull(string template, string variable)
    {
        var path = Path.Combine(RepositoryRoot(), template.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue("the env template is at " + path);

        var match = Regex.Match(
            File.ReadAllText(path),
            $"^{variable}=(?<value>.+)$",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>
    /// The value the env template ships for a variable.
    /// </summary>
    private static string TemplateValue(string template, string variable)
    {
        var path = Path.Combine(RepositoryRoot(), template.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue("the env template is at " + path);

        var match = Regex.Match(
            File.ReadAllText(path),
            $"^{variable}=(?<value>.+)$",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));
        match.Success.Should().BeTrue($"{template} must carry a {variable} line");

        return match.Groups["value"].Value.Trim();
    }

    /// <summary>
    /// A key in the shape production actually holds: the base64-of-PEM that Monobank's
    /// <c>GET /api/merchant/pubkey</c> returns. A placeholder here would trip the readability
    /// guard for the wrong reason.
    /// </summary>
    private static string ReadableMonobankKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(ecdsa.ExportSubjectPublicKeyInfoPem()));
    }

    /// <summary>
    /// 32 bytes of key material, for the QR cipher key the baseline holds.
    /// </summary>
    private static byte[] RandomKeyBytes()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    /// <summary>
    /// The repository root, found from the test assembly's directory.
    /// </summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        // Stop at the repo root, marked by a ".git" entry. In a normal clone that entry is a
        // directory; in a git *worktree* it is a FILE (a gitdir pointer), so accept either.
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
               !File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must run from inside the repository");

        return dir!.FullName;
    }
}
