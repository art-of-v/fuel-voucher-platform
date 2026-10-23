using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// The staging connection string must verify the database's certificate, against staging's
/// own CA, and the compose file must be wired to make that true.
/// </summary>
/// <remarks>
/// <para>
/// Staging shares the host and the Docker network with production, and its connection carries
/// the same data production's does - refresh-token hashes, phone numbers, order amounts and the
/// encrypted voucher payloads. Until now it said <c>SSL Mode=Disable</c>, so anything that could
/// reach the Docker network could read that traffic. <c>VerifyFull</c> is the difference between
/// "encrypted" and "encrypted to a server that proved who it is".
/// </para>
/// <para>
/// A connection string that names a CA is not enough by itself: the server has to serve a
/// certificate from that CA. So the tests below read the shipped compose file, not a copy of it -
/// a copy is what let <c>Verify Full</c> reach production on 2026-10-21.
/// </para>
/// <para>
/// The separation that matters here is the CA: staging must not reuse production's. A shared CA
/// would let anyone who could sign a staging certificate present it to production, and staging
/// is the environment with the weaker secrets.
/// </para>
/// </remarks>
public sealed class StagingConnectionStringTests
{
    private const string StagingCaDirectory = "certs/postgres-staging";
    private const string ProductionCaDirectory = "certs/postgres";

    [Fact]
    public void The_Connection_String_The_Compose_Ships_Is_One_Npgsql_Accepts()
    {
        var connectionString = StagingConnectionString();

        var act = () => new NpgsqlDataSourceBuilder(connectionString).Build();

        act.Should().NotThrow(
            "a connection string Npgsql cannot parse is an exception on start-up; 'Verify Full' "
            + "(with a space) is libpq's spelling and took production down on 2026-10-21");
    }

    [Fact]
    public void The_Connection_String_The_Compose_Ships_Verifies_The_Server()
    {
        var connectionString = StagingConnectionString();

        connectionString.Should().Contain("SSL Mode=VerifyFull",
            "an encrypted connection that does not verify the server is the defect this change is about");
        connectionString.Should().Contain("Root Certificate=/certs/ca.crt",
            "the CA has to be named and mounted, or the verification has nothing to verify against");
        connectionString.Should().NotContain("Trust Server Certificate=true");
        connectionString.Should().NotContain("SSL Mode=Disable");
        connectionString.Should().Contain("Host=postgres",
            "the application reaches the database by its compose service name, not the host's loopback");
    }

    [Fact]
    public void The_Database_Serves_A_Certificate_Signed_By_The_Ca_The_Application_Verifies()
    {
        var compose = StagingCompose();

        compose.Should().Contain("ssl=on",
            "without this the server accepts plaintext and the client's VerifyFull has nothing to verify");
        compose.Should().Contain("ssl_cert_file=/certs/server.crt");
        compose.Should().Contain("ssl_key_file=/certs/server.key");

        compose.Should().Contain($"./{StagingCaDirectory}:/certs:ro",
            "the server reads the certificate and its CA from this directory");
        compose.Should().Contain($"./{StagingCaDirectory}/ca.crt:/certs/ca.crt:ro",
            "the application verifies the server against the CA mounted at the path the "
            + "connection string names");
    }

    [Fact]
    public void Staging_Does_Not_Use_Productions_Ca()
    {
        var compose = StagingCompose();

        compose.Should().NotContain($"{ProductionCaDirectory}/",
            "a staging certificate signed by production's CA would be accepted by production, "
            + "and staging is the environment with the weaker secrets");

        // The deploy writes its own CA, in its own directory, with its own name, and asserts the
        // fingerprint differs from production's. Reading the workflow keeps the wiring honest: a
        // compose file pointed at a CA nobody creates would fail at the deploy, not in review.
        var workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));

        workflow.Should().Contain($"--dir /root/FuelFlow/deploy/{StagingCaDirectory}",
            "the staging deploy must create its CA outside production's directory");
        workflow.Should().Contain("assert-postgres-tls.sh",
            "the deploy must prove the live connection is verified TLS, not merely configure it");
        workflow.Should().Contain($"--different-from /root/FuelFlow/deploy/{ProductionCaDirectory}/ca.crt",
            "the proof must compare against production's CA, or 'different' is an assumption");
    }

    /// <summary>
    /// The staging connection string, read out of the compose file that ships it.
    /// </summary>
    private static string StagingConnectionString()
    {
        var compose = StagingCompose();
        var match = Regex.Match(
            compose,
            @"Database__ConnectionString:\s*""(?<value>[^""]+)""",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        match.Success.Should().BeTrue("the staging compose file must carry Database__ConnectionString");

        // The placeholders are compose's own; the shape of the string is what Npgsql reads.
        return Regex.Replace(match.Groups["value"].Value, @"\$\{[A-Z_]+[^}]*\}", "value");
    }

    private static string StagingCompose()
    {
        var path = Path.Combine(RepositoryRoot(), "deploy", "docker-compose.staging.yml");
        File.Exists(path).Should().BeTrue("the staging compose file is at " + path);

        return File.ReadAllText(path);
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
