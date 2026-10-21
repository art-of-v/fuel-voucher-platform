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
    /// The production connection string, read out of the compose file that ships it.
    /// </summary>
    private static string ProductionConnectionString()
    {
        var composePath = Path.Combine(RepositoryRoot(), "deploy", "docker-compose.prod.yml");
        File.Exists(composePath).Should().BeTrue("the production compose file is at " + composePath);

        var compose = File.ReadAllText(composePath);
        var match = Regex.Match(
            compose,
            @"Database__ConnectionString:\s*""(?<value>[^""]+)""",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        match.Success.Should().BeTrue("the compose file must carry a Database__ConnectionString");

        // The placeholders are compose's own; the shape of the string is what Npgsql reads.
        return Regex.Replace(match.Groups["value"].Value, @"\$\{[A-Z_]+[^}]*\}", "value");
    }

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
