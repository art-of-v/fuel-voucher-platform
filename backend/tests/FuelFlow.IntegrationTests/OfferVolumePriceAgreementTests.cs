using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The drift detector behind <c>deploy/preflight-db/010-offer-volume-price-agreement.sql</c>.
/// </summary>
/// <remarks>
/// <para>
/// This file exists because of a production outage. The offer split's migration refused to apply on
/// 2026-10-15 because two OKKO fuel types carried different per-litre prices across their volumes.
/// The refusal was correct, but it happened at container start, so the API was down before anyone
/// read the message — and staging could not catch it first, because staging has no imported fuel
/// types to drift.
/// </para>
///
/// <para>
/// So the assertion also exists as a read-only query asked before anything is pulled. The question
/// these tests answer is whether <b>that query finds drift</b>, which is the part that can be wrong
/// silently: a detector that returns nothing always looks like a clean system.
/// </para>
///
/// <para>
/// The SQL is read from the file rather than pasted in, so these tests and the preflight cannot
/// drift apart.
/// </para>
///
/// <para>
/// <b>What changed when <c>fuel_packages</c> went.</b> This replaced <c>FuelPackagePriceDriftTests</c>.
/// That one asked how many distinct values a (station, fuel, pool) group held for each per-litre
/// column, because the old table carried the rate on every volume row — so two rows for one fuel
/// could genuinely disagree. The rate now lives on the offer and the unique index makes a group
/// exactly one row, which means that disagreement is no longer representable and the detector would
/// have had nothing to ask. What is still askable is the pair that now holds a price in two places:
/// the offer names a rate, the volume names what that nominal costs, and nothing ties them together.
/// So the detector was rewritten around that disagreement, reported in money — what the customer
/// would be over- or under-charged — because that is the number a human resolving it needs.
/// </para>
///
/// <para>
/// Coverage is not smaller for it. The same seed, reset, multi-row reporting and read-only
/// guarantees are asserted, against the exact file the deploy executes, on the schema that exists
/// now.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class OfferVolumePriceAgreementTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OfferVolumePriceAgreementTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>The file the deploy preflight executes. Resolved from the repository root.</summary>
    /// <summary>The tag every case seeds under, so a station id reads as its own name.</summary>
    private const string Tag = "drift";

    private static string DetectorSql => File.ReadAllText(FindRepoFile(
        Path.Combine("deploy", "preflight-db", "010-offer-volume-price-agreement.sql")));

    [Fact]
    public async Task TheSeededCatalogue_ShowsNoDrift()
    {
        // The happy path, so "no rows" is known to be reachable and not simply what a broken query
        // returns.
        await using var context = CreateContext();

        (await ReadDriftAsync(context)).Should().BeEmpty(
            "the seed builds every volume of a fuel from one price, and the split copied that price "
            + "onto the offer rather than deriving it per volume");
    }

    [Theory]
    [InlineData("2.00", "6.00")]   // volume priced 6 UAH above what the offer's rate says
    [InlineData("4.50", "-12.00")]  // and below, so the sign of the error is not what finds it
    public async Task AVolumeThatDisagreesWithItsOffer_IsFound(string liters, string discrepancy)
    {
        // Money, not a ratio: the whole point of the rewritten detector is that the number on screen
        // is the number a customer would be charged wrongly.
        await using var context = CreateContext();
        await SeedAsync(context, Tag, liters: liters, discrepancy: discrepancy);
        try
        {
            var found = await ReadDriftAsync(context);

            found.Should().ContainSingle("one volume, one row");
            found[0].discrepancy_uah.Should().Be(
                decimal.Parse(discrepancy, System.Globalization.CultureInfo.InvariantCulture));
            found[0].station_id.Should().Be($"drift-stn-{Tag}", "reported under the station whose offer disagrees");
        }
        finally
        {
            await ResetAsync(context);
        }
    }

    [Fact]
    public async Task DisagreementSmallerThanHalfAKopeck_IsNotDrought()
    {
        // A package_price is numeric(12,2) and a per-litre rate numeric(10,4), so multiplying them
        // can land a fraction of a kopeck away from the stored total on data that is actually
        // consistent. Reporting that would put a permanent, unfalsifiable warning on the deploy path,
        // and a gate people learn to click past is not a gate.
        await using var context = CreateContext();
        await SeedAsync(context, "drift", liters: "3.00", discrepancy: "0.004");
        try
        {
            (await ReadDriftAsync(context)).Should().BeEmpty();
        }
        finally
        {
            await ResetAsync(context);
        }
    }

    [Fact]
    public async Task AnOfferWithNoMargin_IsSkipped_RatherThanReported()
    {
        // A supplier cost with no margin is the state a catalogue-imported fuel nobody has priced yet is
        // in: the offer exists, and there is no rate to disagree with. A detector that fired on it would
        // be crying wolf on a state the system deliberately permits.
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_offers (id, station_id, fuel_type_id, supplier_price_per_liter,
                created_at_utc, updated_at_utc, is_deleted, data_kind)
            VALUES ('null-offer', 'null-stn', 'null-fuel', 40.0000, now(), now(), false, 'real');
            INSERT INTO fuel_offer_volumes (id, offer_id, liters, package_price,
                created_at_utc, updated_at_utc, is_deleted)
            VALUES ('null-vol', 'null-offer', 10.00, 999.00, now(), now(), false);
            """);
        try
        {
            (await ReadDriftAsync(context)).Should().BeEmpty(
                "no rate means nothing to disagree with");
        }
        finally
        {
            await ResetAsync(context);
        }
    }

    [Fact]
    public async Task TwoPoolsOfOneFuel_AreNotConfusedForDrift()
    {
        // The QA mirror carries its own offer, priced from its own seed. Pool separation is the whole
        // reason those two can coexist, so a detector that compared across them would report every
        // rehearsed fuel as drift.
        await using var context = CreateContext();
        await SeedAsync(context, "pool-real", liters: "10.00", discrepancy: "0", pool: "real");
        await SeedAsync(context, "pool-qa", liters: "10.00", discrepancy: "0", pool: "qa");
        try
        {
            (await ReadDriftAsync(context)).Should().BeEmpty();
        }
        finally
        {
            await ResetAsync(context);
        }
    }

    [Fact]
    public async Task TwoStationsSellingOneFuel_AreNotConfusedForDrift()
    {
        // Same shape as the pool case, one dimension over: the rate is per offer, and an offer is
        // per station. Two stations may legitimately sell the same fuel at different prices.
        await using var context = CreateContext();
        await SeedAsync(context, "stn-a", liters: "10.00", discrepancy: "0", station: "stn-a");
        await SeedAsync(context, "stn-b", liters: "10.00", discrepancy: "0", station: "stn-b");
        try
        {
            (await ReadDriftAsync(context)).Should().BeEmpty();
        }
        finally
        {
            await ResetAsync(context);
        }
    }

    [Fact]
    public async Task DifferentNominalsOfOneOffer_AreNotDriftOnTheirOwn()
    {
        // 10 L at 500 and 50 L at 2500 agree with each other perfectly. Their package prices differ
        // because their sizes differ — which is the case a naive "do the volumes of this offer have
        // equal package_price" check would report, every time, on every fuel in the catalogue.
        await using var context = CreateContext();
        await SeedAsync(context, "ok-a", liters: "10.00", discrepancy: "0", extraLiters: ("50.00", 0m));
        try
        {
            (await ReadDriftAsync(context)).Should().BeEmpty();
        }
        finally
        {
            await ResetAsync(context);
        }
    }

    [Fact]
    public async Task AnArchivedVolume_IsNotDrift()
    {
        // Never read by the storefront, so a stale price on one cannot publish anything. A blocking
        // check that fires on historical rows trains people to ignore it.
        await using var context = CreateContext();
        await SeedAsync(context, "arch", liters: "10.00", discrepancy: "0", archived: true);
        try
        {
            (await ReadDriftAsync(context)).Should().BeEmpty();
        }
        finally
        {
            await ResetAsync(context);
        }
    }

    [Fact]
    public async Task DriftInTwoOffers_IsReportedOnceForEach()
    {
        await using var context = CreateContext();
        await SeedAsync(context, "two-a", liters: "10.00", discrepancy: "5.00");
        await SeedAsync(context, "two-b", liters: "20.00", discrepancy: "-8.00");
        try
        {
            var found = await ReadDriftAsync(context);

            found.Should().HaveCount(2, "one row per disagreeing volume");
            found.Select(f => f.station_id).Should().BeEquivalentTo(["drift-stn-two-a", "drift-stn-two-b"]);

            // Worst FIRST, so the number that matters is at the top of the deploy log. Ordering is by
            // magnitude, not by sign, so the leading row here is the -8.00 one — a customer
            // undercharged by 8 UAH outranks a customer overcharged by 5, and the sign is carried in
            // the column rather than in the sort.
            found[0].discrepancy_uah.Should().Be(-8.00m, "the larger error leads, whichever way it points");
            found.Select(f => Math.Abs(f.discrepancy_uah)).Should().BeInDescendingOrder();
            found.Select(f => f.discrepancy_uah).Should().BeEquivalentTo([5.00m, -8.00m]);
        }
        finally
        {
            await ResetAsync(context);
        }
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
            DELETE FROM fuel_offer_volumes WHERE id = 'ok-a-10';
            COMMIT;
            """);

        await writeInReadOnly.Should().ThrowAsync<PostgresException>(
            "the same refusal the preflight's own file relies on");
    }

    [Fact]
    public void TheDetectorFile_IsShapedSoItCannotWrite()
    {
        // The structural half of the guarantee above: the file opens a read-only transaction before
        // the query and commits after it. Read as a plain string, because that is exactly how psql -f
        // will see it — so a future edit that drops either statement fails here.
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

    private sealed record DriftRow(string offer_id, string station_id, string fuel_type_id,
                                   string data_kind, string liters, decimal discrepancy_uah);

    /// <summary>
    /// Runs the detector file and materialises its rows.
    /// </summary>
    /// <remarks>
    /// Npgsql will not map a composite type out of a query, so the columns come back as one
    /// delimited string — which is also exactly what the preflight's row-counting
    /// <c>psql -t -A -F'|'</c> sees.
    /// </remarks>
    private static async Task<List<DriftRow>> ReadDriftAsync(ApplicationDbContext context)
    {
        // Everything before the SELECT is the transaction preamble. Wrapping the query and
        // concatenating the columns in SQL is not a test convenience: SqlQueryRaw&lt;string&gt; maps
        // only the FIRST column, so asking it for six returns one value per row and every Split would
        // then be length 1. The block runs from the SELECT to the FIRST semicolon after it — no
        // literal inside the query contains one, and the file ends with `...v.liters;` then
        // `COMMIT;`, so slicing to the end would pull COMMIT into the wrapper.
        var from = DetectorSql.IndexOf("\nSELECT", StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, "the detector file must contain the query under test");
        var to = DetectorSql.IndexOf(';', from);
        var body = DetectorSql[from..to];

        var lines = await context.Database.SqlQueryRaw<string>(
                $"""
                 SELECT offer_id || '|' || station_id || '|' || fuel_type_id || '|' || data_kind || '|'
                        || liters || '|' || discrepancy_uah AS "Value"
                 FROM ({body}) AS drift
                 """)
            .ToListAsync();

        return lines
            .Select(l => l.Split('|'))
            .Select(p => new DriftRow(p[0], p[1], p[2], p[3], p[4], decimal.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
    }

    /// <summary>
    /// One offer plus one volume that disagrees with it by <paramref name="discrepancy"/> UAH — or
    /// agrees exactly, when that is zero. The rate is set from the nominal so the two arithmetic
    /// paths (disagreeing and not) go through the same numbers.
    /// </summary>
    private static Task SeedAsync(
        ApplicationDbContext context,
        string tag,
        string liters,
        string discrepancy,
        string pool = "real",
        string? station = null,
        bool archived = false,
        (string Liters, decimal Offset)? extraLiters = null)
    {
        var offerId = $"ok-{tag}";
        var stationId = station ?? $"drift-stn-{tag}";

        var sql = $"""
            -- Cost 40 + margin 10 = a 50 UAH/l rate, no pump price so the ceiling does not bind. The
            -- offer stopped storing that rate on 2026-10-17; the detector derives it from these two
            -- columns, exactly as the storefront and checkout now do.
            INSERT INTO fuel_offers (id, station_id, fuel_type_id, supplier_price_per_liter,
                margin_uah_per_liter, created_at_utc, updated_at_utc, is_deleted, data_kind)
            VALUES ('{offerId}', '{stationId}', 'drift-fuel-{tag}', 40.0000, 10.0000,
                now(), now(), false, '{pool}');

            INSERT INTO fuel_offer_volumes (id, offer_id, liters, package_price,
                created_at_utc, updated_at_utc, is_deleted)
            VALUES ('ok-{tag}-10', '{offerId}', {liters},
                round(50.0000 * {liters} + {discrepancy}, 2), now(), now(), {(archived ? "true" : "false")});
            """;

        if (extraLiters is { } extra)
        {
            sql += $"""
                INSERT INTO fuel_offer_volumes (id, offer_id, liters, package_price,
                    created_at_utc, updated_at_utc, is_deleted)
                VALUES ('ok-{tag}-50', '{offerId}', {extra.Liters},
                    round(50.0000 * {extra.Liters} + {extra.Offset}, 2), now(), now(), false);
                """;
        }

        return context.Database.ExecuteSqlRawAsync(sql);
    }

    private static Task ResetAsync(ApplicationDbContext context)
        => context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM fuel_offer_volumes WHERE id LIKE 'ok-%-10' OR id LIKE 'ok-%-50' OR id = 'null-vol';
            DELETE FROM fuel_offers WHERE id LIKE 'ok-%' OR id = 'null-offer';
            """);

    /// <summary>Walks up from the test binary to the repository root, then to one of its files.</summary>
    /// <remarks>
    /// The anchor is <c>FuelFlow.slnx</c>, which lives in <c>backend/</c> — so it identifies the
    /// BACKEND root, not the repository root, and <c>deploy/</c> is one level above it. Walking up to
    /// the solution and stopping there finds no <c>deploy</c> directory.
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
