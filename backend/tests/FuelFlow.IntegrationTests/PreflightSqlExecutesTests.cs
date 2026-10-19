using FluentAssertions;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Every SQL file that runs against a live database has to run against the schema that exists
/// today, and a file that does not is worse than no file: it is a check that reports success
/// without having asked anything.
/// </summary>
/// <remarks>
/// <para>
/// The concrete failure this exists for: <c>deploy/preflight-db/011-fuel-type-name-twins.sql</c>
/// grouped by <c>f.station_id</c> from 2026-10-18, and
/// <c>20261018191320_CatalogueBrandsBecomeUuid</c> renamed that column to <c>brand_id</c>. The
/// query had been aborting with <c>column f.station_id does not exist</c> on every deploy since,
/// and the runner reported <c>011-fuel-type-name-twins.sql: ok</c> for every one of them - it sent
/// psql's stderr to /dev/null, counted the empty stdout as zero rows, and psql exits 0 on a SQL
/// error unless it is told otherwise. A gate that could not run was a gate that said yes, for a
/// week, on the production deploy path.
/// </para>
/// <para>
/// So the check is not "the file is shaped right" - <see cref="PreflightDbFileTests"/> owns that,
/// and it cannot see a column that no longer exists. The check here is that the file EXECUTES:
/// the same SQL, against a database built by the same migrations the application runs. A renamed
/// column, a dropped table, a column that changed type - all of them are errors, and all of them
/// are errors the database refuses.
/// </para>
/// <para>
/// The file is executed rather than parsed on purpose. Parsing would need a SQL grammar to know
/// that <c>f.station_id</c> names a column, and a grammar does not know the schema; the database
/// does, and it is the one that refused the query in production.
/// </para>
/// <para>
/// psql's own directives are translated rather than understood, because they are a client
/// feature and not part of the SQL: <c>\set</c>, <c>\echo</c>, <c>\gset</c> and the
/// <c>\if</c>/<c>\else</c>/<c>\endif</c> blocks are removed, because the condition they guard
/// ("does this table exist") is answered differently here - every table in the file exists in a
/// migrated database, so the guarded statements are the ones worth running. The transaction the
/// file opens is replaced with one this test opens, read-only, which is what makes a file that
/// WRITES fail here rather than in production.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class PreflightSqlExecutesTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public PreflightSqlExecutesTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> PreconditionFiles()
    {
        var directory = Path.Combine(RepoRoot(), "deploy", "preflight-db");
        foreach (var file in Directory.GetFiles(directory, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return [Path.GetFileName(file)];
        }
    }

    [Theory]
    [MemberData(nameof(PreconditionFiles))]
    public async Task EveryPreconditionFile_RunsAgainstTheSchemaTheApplicationRuns(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "deploy", "preflight-db", fileName);
        var sql = ToExecutableSql(File.ReadAllText(path));

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        // Read-only, so a file that writes cannot pass here. The deploy runner runs these against
        // production, and a precondition that can modify production is a far worse addition than
        // the outage it is here to prevent - the text-shape test asserts the file SAYS it is
        // read-only, and this asserts it cannot write even if it says so.
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY;\n" + sql, connection, transaction);

        var act = () => command.ExecuteNonQueryAsync();

        await act.Should().NotThrowAsync(
            $"{fileName} does not run against the current schema. A precondition that cannot be " +
            "executed reports success without having checked anything, which is how " +
            "011-fuel-type-name-twins.sql spent a week saying 'ok' over a query PostgreSQL had " +
            "refused.");

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// The file's SQL, with psql's client directives removed and its transaction replaced.
    /// </summary>
    private static string ToExecutableSql(string fileText)
    {
        var sql = new System.Text.StringBuilder();
        var skippingElseBranch = false;

        foreach (var raw in fileText.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();

            // A directive on a line of its own: \set, \echo, \if, \else, \endif, \gset.
            if (trimmed.StartsWith('\\'))
            {
                // The \else branch is not executed: a file that asks "does this table exist" has
                // already been answered by the migrated database the file is run against, and the
                // branch that would have run on a database WITHOUT the table is exactly the one
                // that cannot run here.
                skippingElseBranch = trimmed.StartsWith("\\else", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (skippingElseBranch)
            {
                continue;
            }

            // A directive at the end of a SQL line: "SELECT ... AS t \gset". The \gset replaced
            // the semicolon, so removing it has to put one back - otherwise the next statement is
            // read as a continuation of this one.
            var gset = trimmed.IndexOf("\\gset", StringComparison.Ordinal);
            if (gset >= 0)
            {
                line = line[..gset].TrimEnd();
                if (!line.EndsWith(';'))
                {
                    line += ";";
                }
            }

            var statement = line.TrimStart();
            if (statement.StartsWith("BEGIN;", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("COMMIT;", StringComparison.OrdinalIgnoreCase)
                || statement.StartsWith("SET TRANSACTION READ ONLY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sql.AppendLine(line);
        }

        return sql.ToString();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FuelFlow.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("could not locate the backend root from " + AppContext.BaseDirectory);
        return dir!.Parent!.FullName;
    }
}
