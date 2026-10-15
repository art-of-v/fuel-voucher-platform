using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The drift detector behind <c>deploy/preflight-db/010-fuel-package-single-price.sql</c>.
/// </summary>
/// <remarks>
/// <para>
/// This file exists because of a production outage. The offer split's migration refused to apply
/// on 2026-10-15 because two OKKO fuel types carried different per-litre prices across their
/// volumes. The refusal was correct, but it happened at container start, so the API was down
/// before anyone read the message — and staging could not catch it first, because staging has no
/// imported fuel types to drift.
/// </para>
///
/// <para>
/// So the assertion now also exists as a read-only query asked before anything is pulled. The
/// question these tests answer is whether <b>that query finds drift</b>, which is the part that can
/// be wrong silently: a detector that returns nothing always looks like a clean system.
/// </para>
///
/// <para>
/// The SQL is read from the file rather than pasted in, so these tests and the preflight cannot
/// drift apart. When the offer split is re-landed, its migration guard must use this same query —
/// and that assertion is the thing to add then, because the migration does not exist yet.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FuelPackagePriceDriftTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelPackagePriceDriftTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options);

    /// <summary>The file the deploy preflight executes. Resolved from the repository root.</summary>
    private static string DetectorSql => File.ReadAllText(FindRepoFile(
        Path.Combine("deploy", "preflight-db", "010-fuel-package-single-price.sql")));

    [Fact]
    public async Task TheSeededCatalogue_ShowsNoDrift()
    {
        // The happy path, so "no rows" is known to be reachable and not simply what a broken query
        // returns.
        await using var context = CreateContext();

        (await ReadDriftAsync(context)).Should().BeEmpty(
            "the seed builds every volume of a fuel from one price");
    }

    [Theory]
    [InlineData("final_price_per_liter", "52.0000", "1.0000")]
    [InlineData("pump_price_per_liter", "60.0000", "5.0000")]
    [InlineData("min_discount_per_liter", "1.0000", "4.0000")]
    [InlineData("supplier_price_per_liter", "50.0000", "0.5000")]
    [InlineData("margin_uah_per_liter", "2.0000", "-1.0000")]
    [InlineData("margin_percent", "3.8000", "0.5000")]
    public async Task TwoVolumesOfOneOfferAtDifferentPrices_AreFound(string column, string first, string second)
    {
        // Every per-litre column, not just the headline one. The storefront publishes
        // final_price_per_liter, but the checkout ceiling (pump - minDiscount) and the margin floor
        // both read the others, so a group disagreeing on any of them is still a group whose
        // collapse to one value loses information.
        await using var context = CreateContext();
        await ResetAsync(context);

        // Both rows carry the SAME value in the column under test, then one is changed - so the
        // only difference the detector can find is the one this test created.
        //
        // Two mistakes are encoded in the comments, both of which this test hit first. Writing the
        // differing value in at insert time also made final_price_per_liter disagree, so the
        // detector correctly reported two columns and the theory failed for a reason unrelated to
        // the column it names. And `final_price_per_liter` is already in the base column list, so
        // passing it as the "extra" column named it twice - which is why the extra column is
        // omitted when the column under test is the one the base insert already sets.
        const string headline = "final_price_per_liter";
        var isHeadline = column == headline;

        await InsertPackageAsync(context, "drift-1", "upg", "upg-drift", 10m,
            isHeadline ? first : "52.0000", isHeadline ? null : column, first);
        await InsertPackageAsync(context, "drift-2", "upg", "upg-drift", 20m,
            isHeadline ? first : "52.0000", isHeadline ? null : column, first);

        await context.Database.ExecuteSqlRawAsync(
            $"""UPDATE fuel_packages SET {column} = '{second}'::numeric WHERE id = 'drift-2'""");

        var found = await ReadDriftAsync(context);

        found.Should().ContainSingle();
        found[0].column_name.Should().Be(column);
        found[0].station_id.Should().Be("upg");
        found[0].fuel_type_id.Should().Be("upg-drift");
        found[0].data_kind.Should().Be("real");
        found[0].distinct_values.Should().Be(2);
    }

    [Fact]
    public async Task OneVolumeWithNoPriceAndAnotherWithOne_IsFound()
    {
        // The case a plain count(DISTINCT col) misses. NULL is skipped by that aggregate, so
        // {50, NULL} counts as one distinct value and reads as agreement - precisely the "one volume
        // carries no price" case, and the reason the query counts COALESCE(col::text, '<null>').
        await using var context = CreateContext();
        await ResetAsync(context);

        await InsertPackageAsync(context, "null-1", "upg", "upg-null", 10m, "52.0000");
        await InsertPackageAsync(context, "null-2", "upg", "upg-null", 20m, "52.0000");
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_packages SET final_price_per_liter = NULL WHERE id = 'null-2'""");

        var found = await ReadDriftAsync(context);

        found.Should().ContainSingle();
        found[0].column_name.Should().Be("final_price_per_liter");
    }

    [Fact]
    public async Task TwoPoolsOfOneFuel_AreNotDrift()
    {
        // A QA mirror carries its own price — that is the entire point of a rehearsal catalogue
        // priced at 1 UAH — so grouping without data_kind would report every mirrored offer as
        // drift and block every deploy forever.
        await using var context = CreateContext();
        await ResetAsync(context);

        await InsertPackageAsync(context, "pool-real", "upg", "upg-pool", 10m, "52.0000");
        await InsertPackageAsync(context, "pool-qa", "upg", "upg-pool", 10m, "1.0000");
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_packages SET data_kind = 'qa' WHERE id = 'pool-qa'""");

        (await ReadDriftAsync(context)).Should().BeEmpty("two pools are two offers, not one in disagreement");
    }

    [Fact]
    public async Task TwoStationsSellingOneFuel_AreNotDrift()
    {
        // The same argument for station_id: the storefront groups and resolves on the pair, so a
        // fuel two networks list at different prices is normal.
        await using var context = CreateContext();
        await ResetAsync(context);

        await InsertPackageAsync(context, "stn-okko", "okko", "upg-stn", 10m, "52.0000");
        await InsertPackageAsync(context, "stn-wog", "wog", "upg-stn", 10m, "48.0000");

        (await ReadDriftAsync(context)).Should().BeEmpty();
    }

    [Fact]
    public async Task DifferentVolumesOfOneOffer_AreNotDriftOnTheirOwn()
    {
        // The shape of a healthy offer: several sizes, one price. A detector that keyed on anything
        // but the price columns would report this as drift on every real offer.
        await using var context = CreateContext();
        await ResetAsync(context);

        await InsertPackageAsync(context, "ok-10", "upg", "upg-ok", 10m, "52.0000");
        await InsertPackageAsync(context, "ok-20", "upg", "upg-ok", 20m, "52.0000");
        await InsertPackageAsync(context, "ok-50", "upg", "upg-ok", 50m, "52.0000");

        (await ReadDriftAsync(context)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnArchivedVolume_IsNotDrift()
    {
        // Archived rows are excluded on purpose: the storefront never reads them, so a stale price
        // on one cannot publish anything. A blocking check that fires on historical rows trains
        // people to ignore it.
        await using var context = CreateContext();
        await ResetAsync(context);

        await InsertPackageAsync(context, "arch-live", "upg", "upg-arch", 10m, "52.0000");
        await InsertPackageAsync(context, "arch-dead", "upg", "upg-arch", 20m, "1.0000");
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_packages SET is_deleted = true WHERE id = 'arch-dead'""");

        (await ReadDriftAsync(context)).Should().BeEmpty();
    }

    [Fact]
    public async Task DriftInTwoGroups_IsReportedOnceForEachColumn()
    {
        // The shape the production failure had: two fuel types, and one row per offending column
        // rather than one row per group. Reported per column so a group disagreeing on all six
        // prices does not collapse into a single unreadable line.
        await using var context = CreateContext();
        await ResetAsync(context);

        await InsertPackageAsync(context, "two-a1", "upg", "upg-two-a", 10m, "52.0000");
        await InsertPackageAsync(context, "two-a2", "upg", "upg-two-a", 20m, "52.0000");
        await InsertPackageAsync(context, "two-b1", "upg", "upg-two-b", 10m, "52.0000");
        await InsertPackageAsync(context, "two-b2", "upg", "upg-two-b", 20m, "52.0000");

        await context.Database.ExecuteSqlRawAsync("""
            UPDATE fuel_packages SET final_price_per_liter = 1.0000 WHERE id = 'two-a2';
            UPDATE fuel_packages SET final_price_per_liter = 2.0000 WHERE id = 'two-b2';
            """);

        var found = await ReadDriftAsync(context);

        found.Should().HaveCount(2);
        found.Select(f => f.fuel_type_id).Should().BeEquivalentTo(["upg-two-a", "upg-two-b"]);
    }

    [Fact]
    public async Task TheDetector_RunsInsideAReadOnlyTransaction()
    {
        // The preflight runs on the production box, so "this only reads" has to be enforced rather
        // than asserted in a comment.
        //
        // It started as `SET default_transaction_read_only = on;`, which does NOT do it: psql runs
        // each statement in autocommit, so the setting applied to a transaction that then ended, and
        // a write appended after the query got through. The file now wraps itself in
        // BEGIN / SET TRANSACTION READ ONLY / COMMIT, and this test proves Postgres refuses a write
        // inside one — because a guarantee believed on the strength of a comment is worse than none.
        await using var context = CreateContext();

        var writeInReadOnly = async () => await context.Database.ExecuteSqlRawAsync(
            """
            BEGIN;
            SET TRANSACTION READ ONLY;
            DELETE FROM fuel_packages WHERE id = 'okko-95-10';
            COMMIT;
            """);

        await writeInReadOnly.Should().ThrowAsync<PostgresException>(
            "the same refusal the preflight's own file relies on");

        await using var verify = CreateContext();
        verify.FuelPackages.IgnoreQueryFilters().Any(p => p.Id == "okko-95-10")
            .Should().BeTrue("and the row it tried to delete is still there");
    }

    [Fact]
    public void TheDetectorFile_IsShapedSoItCannotWrite()
    {
        // The structural half of the guarantee above: the file opens a read-only transaction
        // before the query and commits after it. Read as a plain string, because that is exactly how
        // psql -f will see it - so a future edit that drops either statement fails here.
        var sql = DetectorSql;

        // Matched with the leading newline, NOT bare substrings. The header comment explains the
        // mechanism and therefore contains the words "BEGIN / SET TRANSACTION READ ONLY / COMMIT";
        // a bare IndexOf finds the explanation first and reports the file in the wrong order.
        var begin = sql.IndexOf("\nBEGIN;", StringComparison.Ordinal);
        var readOnly = sql.IndexOf("\nSET TRANSACTION READ ONLY;", StringComparison.Ordinal);
        var commit = sql.LastIndexOf("\nCOMMIT;", StringComparison.Ordinal);

        begin.Should().BeGreaterThan(-1, "without BEGIN the transaction binding does not exist");
        readOnly.Should().BeGreaterThan(begin, "the setting has to be inside the transaction it governs");
        commit.Should().BeGreaterThan(readOnly, "and the transaction has to be closed at the end");
    }

    // -- helpers ------------------------------------------------------------

    private sealed record DriftRow(string station_id, string fuel_type_id, string data_kind,
                                   int volumes, string column_name, int distinct_values);

    /// <summary>
    /// Runs the detector file and materialises its rows.
    /// </summary>
    /// <remarks>
    /// Npgsql will not map a composite type out of a query — "not supported for fields having
    /// DataTypeName 'uuid'", and the same complaint about 'text' for a ValueTuple. So the six
    /// columns come back as one delimited string, which is also exactly what the preflight's
    /// row-counting `psql -t -A -F'|'` sees.
    /// </remarks>
    private static async Task<List<DriftRow>> ReadDriftAsync(ApplicationDbContext context)
    {
        // Everything before the final SELECT is setup (the SET, then the CTE). Wrapping the query
        // and concatenating the six columns in SQL is not a test convenience: SqlQueryRaw<string>
        // maps only the FIRST column, so asking it for six returns one value per row and every
        // Split would then be length 1. Concatenating here also makes this read exactly what the
        // preflight's `psql -t -A -F'|'` sees, so the test and the script agree on what a row is.
        // The block runs from the CTE to the FIRST semicolon, and no earlier: the file ends with
        // `...d.column_name;` then `COMMIT;`, so slicing to the end pulled COMMIT into the wrapper
        // and every read failed with "syntax error at or near ;". None of the literals inside the
        // query contain a semicolon, so the first one after the CTE is the end of the statement.
        var from = DetectorSql.IndexOf("WITH per_group AS", StringComparison.Ordinal);
        var to = DetectorSql.IndexOf(';', from);
        var body = DetectorSql[from..to];

        var lines = await context.Database.SqlQueryRaw<string>(
                $"""
                 SELECT station_id || '|' || fuel_type_id || '|' || data_kind || '|'
                        || volumes || '|' || column_name || '|' || distinct_values AS "Value"
                 FROM ({body}) AS drift
                 """)
            .ToListAsync();

        return lines
            .Select(l => l.Split('|'))
            .Select(p => new DriftRow(p[0], p[1], p[2], int.Parse(p[3]), p[4], int.Parse(p[5])))
            .ToList();
    }

    private static Task ResetAsync(ApplicationDbContext context)
        => context.Database.ExecuteSqlRawAsync(
            """DELETE FROM fuel_packages WHERE id LIKE 'drift-%' OR id LIKE 'null-%' OR id LIKE 'pool-%' OR id LIKE 'stn-%' OR id LIKE 'ok-%' OR id LIKE 'arch-%' OR id LIKE 'two-%'""");

    private static Task InsertPackageAsync(
        ApplicationDbContext context,
        string id,
        string stationId,
        string fuelTypeId,
        decimal liters,
        string finalPrice,
        string? alsoColumn = null,
        string? alsoValue = null)
    {
        // The extra column has to be appended to the COLUMN LIST and its value to the VALUES
        // tuple. Writing `, col = value` inside the tuple is not SQL - Postgres reads it as a column
        // reference and answers "column does not exist", which reads like a schema problem and is
        // not one.
        // Both halves keyed on alsoColumn, NOT on alsoValue: keying the value on alsoValue emits a value
        // with no column to put it in, and the insert fails with "more expressions than target
        // columns" — which is what happened when the column under test was final_price_per_liter and
        // therefore carried no extra column but still carried an extra value.
        var extraColumns = alsoColumn is null ? "" : $", {alsoColumn}";
        var extraValues = alsoColumn is null ? "" : $", {alsoValue}::numeric";

        return context.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO fuel_packages (id, station_id, fuel_type_id, fuel_name, liters, price,
                                       original_price, final_price_per_liter, data_kind, is_deleted,
                                       created_at_utc, updated_at_utc{extraColumns})
            VALUES ('{id}', '{stationId}', '{fuelTypeId}', 'drift', {liters}, 520, 520,
                    {finalPrice}::numeric, 'real', false, now(), now(){extraValues})
            """);
    }

    /// <summary>Walks up from the test binary to the repository root, then to one of its files.</summary>
    /// <remarks>
    /// The anchor is <c>FuelFlow.slnx</c>, which lives in <c>backend/</c> — so it identifies the
    /// BACKEND root, not the repository root, and <c>deploy/</c> is one level above it. Walking up to
    /// the solution and stopping there finds no <c>deploy</c> directory, which is how this test
    /// first failed with "file not found" rather than with a wrong answer.
    /// </remarks>
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