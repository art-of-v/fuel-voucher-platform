using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The migration's guard refuses to collapse volume rows that disagree on the per-litre price.
/// </summary>
/// <remarks>
/// <para>
/// The guard is the part of <c>SplitFuelPackagesIntoOffers</c> that cannot be exercised by the
/// normal replay: the fixture's data is uniform, so the guard never fires and a regression that
/// removed it would pass every other test in the suite.
/// </para>
/// <para>
/// So the drift is manufactured and the <em>guard's own statement</em> is run. Not the migration -
/// rolling the schema back to insert drifted rows, then forward, would prove the same thing, but it
/// needs the rows to survive a Down()/Up() round trip, which makes this test a test of two
/// migrations at once. Running the statement alone keeps the failure attributable.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class OfferSplitGuardTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OfferSplitGuardTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options);

    /// <summary>
    /// The guard body, verbatim from the migration. Copied rather than referenced so this test
    /// documents the rule; if the migration's copy changes, the comment on this method says so.
    /// </summary>
    private const string Guard = """
        DO $$
        DECLARE
            drifted text;
        BEGIN
            SELECT string_agg(offer_label, ', ' ORDER BY offer_label) INTO drifted
            FROM (
                SELECT station_id || '/' || fuel_type_id || '/' || data_kind AS offer_label
                FROM fuel_packages
                GROUP BY station_id, fuel_type_id, data_kind
                HAVING count(DISTINCT COALESCE(final_price_per_liter::text, '<null>')) > 1
                    OR count(DISTINCT COALESCE(pump_price_per_liter::text, '<null>')) > 1
                    OR count(DISTINCT COALESCE(min_discount_per_liter::text, '<null>')) > 1
                    OR count(DISTINCT COALESCE(supplier_price_per_liter::text, '<null>')) > 1
                    OR count(DISTINCT COALESCE(margin_uah_per_liter::text, '<null>')) > 1
                    OR count(DISTINCT COALESCE(margin_percent::text, '<null>')) > 1
            ) d;

            IF drifted IS NOT NULL THEN
                RAISE EXCEPTION
                    'fuel_packages carries more than one per-litre price for: %. '
                    'The offer split assumes exactly one price per (station, fuel, pool); '
                    'reconcile these rows before retrying.',
                    drifted;
            END IF;
        END $$;
        """;

    [Fact]
    public async Task TheSeededCatalogue_PassesTheGuard()
    {
        // The happy path, so the guard is known to be satisfiable and not merely always-throwing.
        await using var context = CreateContext();
        await ResetDriftAsync(context);

        var act = async () => await context.Database.ExecuteSqlRawAsync(Guard);
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("final_price_per_liter", "52.0000", "1.0000")]
    [InlineData("pump_price_per_liter", "60.0000", "5.0000")]
    [InlineData("supplier_price_per_liter", "50.0000", "0.5000")]
    [InlineData("margin_uah_per_liter", "2.0000", "-1.0000")]
    [InlineData("margin_percent", "3.8000", "0.5000")]
    [InlineData("min_discount_per_liter", "1.0000", "4.0000")]
    public async Task TwoVolumesOfOneOfferAtDifferentPrices_AreRefused(string column, string first, string second)
    {
        // Every per-litre column is checked, not just the headline one. The storefront publishes
        // final_price_per_liter, but the checkout ceiling (pump - minDiscount) and the margin floor
        // both read the others, so a group that disagreed on any of them would still be a group whose
        // collapsing to one value loses information.
        await using var context = CreateContext();
        await ResetDriftAsync(context);

        await InsertPackage("guard-1", "upg", "upg-guard1", 10m, "52.0000");
        await InsertPackage("guard-2", "upg", "upg-guard1", 20m, first);
        await context.Database.ExecuteSqlRawAsync(
            $"""UPDATE fuel_packages SET {column} = '{second}'::numeric WHERE id = 'guard-2'""");

        var act = async () => await context.Database.ExecuteSqlRawAsync(Guard);
        await act.Should().ThrowAsync<PostgresException>(
            "the split cannot pick a winner between two prices, so it must refuse rather than choose");
    }

    [Fact]
    public async Task OneVolumeWithNoPriceAndAnotherWithOne_AreRefused()
    {
        // The case a plain count(DISTINCT col) would miss. NULL is skipped by that aggregate, so
        // {50, NULL} counts as one distinct value and the guard would wave it through - collapsing a
        // priced volume and an unpriced one into a single offer whose price it then invents.
        await using var context = CreateContext();
        await ResetDriftAsync(context);

        await InsertPackage("guard-null-1", "upg", "upg-guardnull", 10m, "52.0000");
        await InsertPackage("guard-null-2", "upg", "upg-guardnull", 20m, "52.0000");
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_packages SET final_price_per_liter = NULL WHERE id = 'guard-null-2'""");

        var act = async () => await context.Database.ExecuteSqlRawAsync(Guard);
        await act.Should().ThrowAsync<PostgresException>(
            "one volume carrying no price is a disagreement the guard has to see, which is why it " +
            "counts COALESCE(col::text, '<null>') rather than the column");
    }

    [Fact]
    public async Task TwoOffersOfDifferentPools_AreNotConfusedForDrift()
    {
        // A QA mirror carries its own price - that is the whole point of a rehearsal catalogue priced
        // at 1 UAH - so the guard must group by pool as well as by station and fuel. Grouping without
        // data_kind would make every mirrored offer look like drift and block the deploy forever.
        await using var context = CreateContext();
        await ResetDriftAsync(context);

        await InsertPackage("guard-pool-real", "upg", "upg-guardpool", 10m, "52.0000");
        await InsertPackage("guard-pool-qa", "upg", "upg-guardpool", 10m, "1.0000");
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_packages SET data_kind = 'qa' WHERE id = 'guard-pool-qa'""");

        var act = async () => await context.Database.ExecuteSqlRawAsync(Guard);
        await act.Should().NotThrowAsync("two pools are two offers, not one offer in disagreement");
    }

    [Fact]
    public async Task TheMessageNamesTheOffersThatDrifted()
    {
        // A migration that aborts with "something is wrong" costs a bisect through production data.
        // The exception text has to say which (station, fuel, pool) to look at, because that is
        // exactly what someone has to go and reconcile by hand.
        await using var context = CreateContext();
        await ResetDriftAsync(context);

        await InsertPackage("guard-msg-1", "upg", "upg-guardmsg", 10m, "52.0000");
        await InsertPackage("guard-msg-2", "upg", "upg-guardmsg", 20m, "52.0000");
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_packages SET final_price_per_liter = 1.0000 WHERE id = 'guard-msg-2'""");

        var act = async () => await context.Database.ExecuteSqlRawAsync(Guard);
        var thrown = await act.Should().ThrowAsync<PostgresException>();

        thrown.Which.MessageText.Should().Contain("upg/upg-guardmsg/real");
        thrown.Which.MessageText.Should().Contain("reconcile these rows");
    }

    /// <summary>
    /// Clears the drift the other tests manufacture.
    /// </summary>
    /// <remarks>
    /// One database serves the whole class and xUnit does not promise an order, so a row left behind
    /// by the "two prices must be refused" case would make the "must not throw" cases fail for the
    /// wrong reason - and the error message would name a fuel type the reader never mentioned. The
    /// seeded rows are untouched, so <c>TheSeededCatalogue_PassesTheGuard</c> still means what it says.
    /// </remarks>
    private static Task ResetDriftAsync(ApplicationDbContext context)
        => context.Database.ExecuteSqlRawAsync("""DELETE FROM fuel_packages WHERE id LIKE 'guard-%'""");

    private static async Task InsertPackage(
        string id,
        string stationId,
        string fuelTypeId,
        decimal liters,
        string finalPrice)
    {
        // Raw SQL rather than the entity: fuel_packages is not the subject under test here, only the
        // input the guard reads, and going through the context would drag every mapped column into it.
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(Environment.GetEnvironmentVariable("Database__ConnectionString")!)
                .Options);

        await context.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO fuel_packages (id, station_id, fuel_type_id, fuel_name, liters, price,
                                       original_price, final_price_per_liter, data_kind, is_deleted,
                                       created_at_utc, updated_at_utc)
            VALUES ('{id}', '{stationId}', '{fuelTypeId}', 'guard', {liters}, 520, 520,
                    {finalPrice}::numeric, 'real', false, now(), now())
            """);
    }
}