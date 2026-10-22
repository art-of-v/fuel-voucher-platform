using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// The connection string the production compose file ships must be one Npgsql accepts.
/// </summary>
/// <remarks>
/// <para>
/// On 2026-10-21 the production deploy put this string into the running application and the
/// application died on start-up: Npgsql spells its ssl mode without a space — <c>VerifyFull</c>,
/// not <c>Verify Full</c> — and a value it cannot parse is an exception before the first
/// connection is opened. The database came up with TLS on and the application could not reach it,
/// so the production stack was down until the box was fixed by hand.
/// </para>
/// <para>
/// The guard that was supposed to catch this checked a COPY of the string in a test. A copy is
/// not the string: it drifted the moment the file changed. This test reads the shipped file
/// itself, so the two cannot drift.
/// </para>
/// </remarks>
public sealed class ProductionConnectionStringTests
{
    [Fact]
    public void The_Connection_String_The_Compose_Ships_Is_One_Npgsql_Accepts()
    {
        var connectionString = ProductionConnectionString();

        var act = () => new NpgsqlDataSourceBuilder(connectionString).Build();

        act.Should().NotThrow(
            "a connection string Npgsql cannot parse is an exception on start-up, and the "
            + "production stack goes down with it");
    }

    [Fact]
    public void The_Connection_String_The_Compose_Ships_Verifies_The_Server()
    {
        var connectionString = ProductionConnectionString();

        connectionString.Should().Contain("SSL Mode=VerifyFull",
            "an encrypted connection that does not verify the server is the defect this whole change is about");
        connectionString.Should().Contain("Root Certificate=",
            "the CA has to be named, or the verification has nothing to verify against");
        connectionString.Should().NotContain("Trust Server Certificate=true");
        connectionString.Should().NotContain("SSL Mode=Disable");
    }

    /// <summary>
    /// The application's traffic must not run as the cluster's superuser.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One role used to serve both the application's traffic and the schema migrations,
    /// and it was the superuser the postgres image creates. That is not "an API bug
    /// leaks a table" - it is "an API bug owns the cluster": it could read every role's
    /// password hash out of <c>pg_authid</c>, create a new superuser, drop the database,
    /// or write straight into the audit trail the money reconciliation depends on.
    /// </para>
    /// <para>
    /// The assertion is about the VARIABLE, not about a name: the connection string must
    /// not name <c>${POSTGRES_USER}</c>, because that variable IS the superuser - and it
    /// must not name <c>${POSTGRES_PASSWORD}</c> either, because that is the superuser's
    /// password. A connection string that named either would be a copy of the old one
    /// wearing a different role's name.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_Application_Does_Not_Connect_As_The_Superuser()
    {
        var connectionString = ProductionConnectionString();

        connectionString.Should().Contain("Username=fuelflow_app",
            "the application's traffic belongs to a role that may do the application's work and nothing else");
        connectionString.Should().NotContain("${POSTGRES_USER}",
            "that variable is the cluster's superuser, and the application must never connect as it");
        connectionString.Should().NotContain("${POSTGRES_PASSWORD}",
            "that is the superuser's password, and the application must never hold it");
    }

    /// <summary>
    /// The schema migration must run as a role of its own.
    /// </summary>
    /// <remarks>
    /// A migration alters the tables it finds, so it needs rights the traffic must not
    /// have. That is the whole reason the two connections exist - and it only holds if
    /// they name two different roles.
    /// </remarks>
    [Fact]
    public void The_Migration_Connects_As_A_Role_Of_Its_Own()
    {
        var migration = ComposeEnvironmentValue("Database__MigrationConnectionString");
        var application = ComposeEnvironmentValue("Database__ConnectionString");

        migration.Should().Contain("Username=fuelflow_migrator",
            "the schema belongs to a role that may change it and nothing else");
        migration.Should().NotContain("${POSTGRES_USER}",
            "the superuser is the operator's credential, not the migration's");

        RoleOf(migration).Should().NotBe(RoleOf(application),
            "one role for both is what made a defect that reached the database able to own the cluster");
    }

    /// <summary>
    /// The production connection string, read out of the compose file that ships it.
    /// </summary>
    private static string ProductionConnectionString() =>
        ComposeEnvironmentValue("Database__ConnectionString");

    /// <summary>
    /// One environment value of the production compose file, read out of the file that
    /// ships it.
    /// </summary>
    private static string ComposeEnvironmentValue(string key)
    {
        var composePath = Path.Combine(RepositoryRoot(), "deploy", "docker-compose.prod.yml");
        File.Exists(composePath).Should().BeTrue("the production compose file is at " + composePath);

        var compose = File.ReadAllText(composePath);
        var match = Regex.Match(
            compose,
            $@"{Regex.Escape(key)}:\s*""(?<value>[^""]+)""",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        match.Success.Should().BeTrue($"the compose file must carry {key}");

        // The placeholders are compose's own; the shape of the string is what Npgsql reads.
        return Regex.Replace(match.Groups["value"].Value, @"\$\{[A-Z_]+[^}]*\}", "value");
    }

    /// <summary>
    /// The role a shipped connection string connects as.
    /// </summary>
    private static string RoleOf(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Username ?? string.Empty;

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
