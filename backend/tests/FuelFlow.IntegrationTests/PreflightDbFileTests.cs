using FluentAssertions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Every file in <c>deploy/preflight-db</c> runs against production before an image is pulled, so
/// "this only reads" has to be enforced rather than asserted in a comment.
/// </summary>
/// <remarks>
/// <para>
/// The guarantee is <c>BEGIN / SET TRANSACTION READ ONLY / COMMIT</c> around the query, and not
/// <c>SET default_transaction_read_only = on</c>: psql runs each statement in autocommit, so that
/// setting bound a transaction which then ended, and a write appended after the query went through.
/// Wrapping the query binds the setting to the transaction that actually runs it, and
/// <c>OfferVolumePriceAgreementTests</c> proves Postgres refuses a write inside one — because a
/// guarantee believed on the strength of a comment is worse than none.
/// </para>
/// <para>
/// Stated for the whole directory rather than for the one file that existed when it was learned, so
/// the next check added here inherits it instead of re-deciding. Read as plain text, because that is
/// how <c>psql -f</c> will see it.
/// </para>
/// </remarks>
public sealed class PreflightDbFileTests
{
    [Theory]
    [InlineData("011-fuel-type-name-twins.sql")]
    [InlineData("012-offer-station-matches-its-fuel-type.sql")]
    public void EveryCheck_IsShapedSoItCannotWrite(string fileName)
    {
        var sql = File.ReadAllText(FindRepoFile(Path.Combine("deploy", "preflight-db", fileName)));

        // Matched with the leading newline, NOT as bare substrings. The headers explain the mechanism
        // and therefore contain the words "BEGIN / SET TRANSACTION READ ONLY / COMMIT"; a bare
        // IndexOf finds the explanation first and reports the file in the wrong order — which is how
        // OfferSplitGuardTests passed once with the filter it exists to assert deleted.
        var begin = sql.IndexOf("\nBEGIN;", StringComparison.Ordinal);
        var readOnly = sql.IndexOf("\nSET TRANSACTION READ ONLY;", StringComparison.Ordinal);
        var commit = sql.LastIndexOf("\nCOMMIT;", StringComparison.Ordinal);

        begin.Should().NotBe(-1, $"{fileName} has no transaction, so the read-only binding has nothing to attach to");
        readOnly.Should().BeGreaterThan(begin, $"{fileName}: the setting has to be inside the transaction it governs");
        commit.Should().BeGreaterThan(readOnly, $"{fileName}: and the transaction has to be closed at the end");
    }

    [Fact]
    public void EveryCheckInTheDirectory_IsCoveredByThisTest()
    {
        // A file that ships without a row here is a file nobody asserted the shape of — the same
        // silent gap as a preflight check nobody reads. Enumerating the directory rather than trusting
        // the [InlineData] list is what makes the list exhaust the directory.
        var directory = Path.GetDirectoryName(
            FindRepoFile(Path.Combine("deploy", "preflight-db", "011-fuel-type-name-twins.sql")))!;
        var files = Directory.GetFiles(directory, "*.sql").Select(Path.GetFileName).ToList();

        files.Should().NotBeEmpty();

        var covered = typeof(PreflightDbFileTests)
            .GetMethods()
            .SelectMany(method => method
                .GetCustomAttributes(typeof(InlineDataAttribute), false)
                .Cast<InlineDataAttribute>()
                .SelectMany(attribute => attribute.GetData(method)))
            .Select(data => data[0] as string)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal)!;

        files.Should().AllSatisfy(f => covered.Should().Contain(f));
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FuelFlow.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("could not locate the backend root from " + AppContext.BaseDirectory);

        var repoRoot = dir!.Parent;
        repoRoot.Should().NotBeNull("the backend root has a parent, which is the repository root");

        var path = Path.Combine(repoRoot!.FullName, relativePath);
        File.Exists(path).Should().BeTrue($"the preflight executes {relativePath}, so it must exist");
        return path;
    }
}