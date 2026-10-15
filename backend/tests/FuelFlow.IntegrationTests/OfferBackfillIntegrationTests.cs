using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_packages</c> modelled a volume as a product, duplicating one offer's per-litre pricing
/// across every volume row. This suite covers the split into <c>fuel_offers</c> (the offer) and
/// <c>fuel_offer_volumes</c> (the sizes it is sold in). Planning #318, finding F-33.
/// <para>
/// <b>Nothing in the application reads the new tables yet</b> - that is PR B of the staged split. So
/// none of these tests can go through a handler, and all of them talk to the database directly. That
/// is deliberate: the claim under test is about the schema and the migration, and the only honest way
/// to test a migration is to run it against a database that has rows to migrate.
/// </para>
/// <para>
/// The suite fixture builds its template by replaying every migration from zero, so the
/// <c>fuel_packages</c> rows this backfill reads are the 32 seeded ones - all in the real pool, none
/// in QA, none archived. That is thinner than production (13 real offers + 13 QA mirrors, some
/// archived), so the cases production actually has and the fixture does not are constructed here
/// explicitly rather than assumed.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class OfferBackfillIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OfferBackfillIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options);

    [Fact]
    public async Task EveryPackageRow_BecameExactlyOneVolume()
    {
        // The row count is the first assertion because the failure mode is not "wrong price" but
        // "no row". A backfill that dropped or duplicated rows passes any value-level check that only
        // looks at the rows that survived.
        await using var context = CreateContext();

        var packages = await context.FuelPackages.IgnoreQueryFilters().CountAsync();
        var volumes = await context.FuelOfferVolumes.IgnoreQueryFilters().CountAsync();

        volumes.Should().Be(packages, "one volume per package row, no more and no fewer");
    }

    [Fact]
    public async Task VolumesKeepTheIdOfThePackageRowThey_Replace()
    {
        // An id quoted in a log, a support ticket or a saved cart must still resolve to the same
        // thing after the split. This is the property that makes the split invisible from outside.
        await using var context = CreateContext();

        var packageIds = await context.FuelPackages.IgnoreQueryFilters()
            .Select(p => p.Id).ToListAsync();

        var volumeIds = await context.FuelOfferVolumes.IgnoreQueryFilters()
            .Select(v => v.Id).ToListAsync();

        volumeIds.Should().BeEquivalentTo(packageIds);
    }

    [Fact]
    public async Task EveryVolumeRow_PointedAtAnExistingOffer()
    {
        await using var context = CreateContext();

        var orphans = await context.FuelOfferVolumes
            .IgnoreQueryFilters()
            .Where(v => !context.FuelOffers.IgnoreQueryFilters().Any(o => o.Id == v.OfferId))
            .CountAsync();

        orphans.Should().Be(0);
    }

    [Fact]
    public async Task EveryOfferCarriesThePriceItsVolumeRowsAgreedOn()
    {
        // The defect being fixed, asserted against the data it came from.
        //
        // A volume row has no per-litre price to disagree with - that is the point of the split, and
        // the test above proves the column is absent. So the disagreement to rule out is between the
        // old rows and the offer they collapsed into: had two package rows for one (station, fuel,
        // pool) carried different prices, the migration's guard would have aborted the deploy, and
        // had it somehow not fired, this is the assertion that would notice.
        //
        // This one reads fuel_packages on purpose. It is the only place in the suite where the old
        // table is the subject rather than a leftover: PR C drops those columns, and the assertion
        // goes with them, having by then served its purpose as the check on the backfill.
        await using var context = CreateContext();

        var disagreeing = await context.FuelPackages.IgnoreQueryFilters()
            .Where(p =>
                context.FuelPackages.IgnoreQueryFilters().Any(other =>
                    other.StationId == p.StationId
                    && other.FuelTypeId == p.FuelTypeId
                    && other.DataKind == p.DataKind
                    && other.FinalPricePerLiter != p.FinalPricePerLiter))
            .CountAsync();

        disagreeing.Should().Be(0, "one offer, one per-litre price - the premise the whole split rests on");

        // And the offer took exactly that price, for every seeded fuel.
        var mismatched = await context.FuelPackages.IgnoreQueryFilters()
            .Where(p =>
                !context.FuelOffers.IgnoreQueryFilters().Any(o =>
                    o.StationId == p.StationId
                    && o.FuelTypeId == p.FuelTypeId
                    && o.DataKind == p.DataKind
                    && o.FinalPricePerLiter == p.FinalPricePerLiter))
            .CountAsync();

        mismatched.Should().Be(0, "the backfill copied the price rather than inventing one");
    }

    [Fact]
    public async Task VolumesCarryNoPerLitrePricing()
    {
        // The reason the split exists. A per-litre column reachable from a volume row is a copy that
        // can drift from the offer's, and nothing would detect it.
        await using var context = CreateContext();

        var columns = await context.Database.SqlQueryRaw<string>(
            """
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'fuel_offer_volumes'
              AND (column_name LIKE '%price_per_liter%' OR column_name LIKE 'original_price')
            """).ToListAsync();

        columns.Should().BeEmpty("per-litre pricing belongs to the offer; the volume has litres and a package price");

        // Three, not five: margin_uah_per_liter and min_discount_per_liter are per-litre facts whose
        // column names do not contain "price", and asserting the count by listing the names keeps a
        // future column from passing unnoticed.
        var offerColumns = await context.Database.SqlQueryRaw<string>(
            """
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'fuel_offers'
              AND (column_name LIKE '%price_per_liter%'
                   OR column_name IN ('margin_uah_per_liter', 'min_discount_per_liter'))
            ORDER BY column_name
            """).ToListAsync();

        offerColumns.Should().BeEquivalentTo([
            "final_price_per_liter",
            "margin_uah_per_liter",
            "min_discount_per_liter",
            "pump_price_per_liter",
            "supplier_price_per_liter"
        ], "cost, margin, final, pump and the minimum discount - each stored once, on the offer");
    }

    [Fact]
    public async Task TheOfferIds_AreExactlyWhatTheApplicationWouldCompute()
    {
        // The key derivation is stated twice - in OfferKeys and inline in the migration's SQL,
        // because a migration cannot call into the assembly it migrates from. This is what keeps the
        // two copies of that rule from drifting apart.
        await using var context = CreateContext();

        var rows = await context.FuelOffers.IgnoreQueryFilters()
            .Select(o => new { o.Id, o.StationId, o.FuelTypeId, o.DataKind })
            .ToListAsync();

        rows.Should().NotBeEmpty();

        rows.Should().AllSatisfy(o =>
            o.Id.Should().Be(OfferKeys.For(o.StationId, o.FuelTypeId, o.DataKind)));
    }

    [Fact]
    public async Task OneOfferPerPool_EvenWhereTheSeedHasOnlyTheRealOne()
    {
        // Production has a QA mirror of every live offer; the seed has none. Constructed here so the
        // case that matters - two offers differing only by pool - is actually covered rather than
        // assumed, because if the suffix were dropped these two rows would collide on the unique
        // index and the whole backfill would abort.
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        context.FuelOffers.Add(new FuelOffer
        {
            Id = OfferKeys.For("upg", "upg-pool", DataKinds.Real),
            StationId = "upg",
            FuelTypeId = "upg-pool",
            DataKind = DataKinds.Real,
            FinalPricePerLiter = 51m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        context.FuelOffers.Add(new FuelOffer
        {
            Id = OfferKeys.For("upg", "upg-pool", DataKinds.Qa),
            StationId = "upg",
            FuelTypeId = "upg-pool",
            DataKind = DataKinds.Qa,
            FinalPricePerLiter = 1m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await context.SaveChangesAsync();

        var pools = await context.FuelOffers.IgnoreQueryFilters()
            .Where(o => o.StationId == "upg" && o.FuelTypeId == "upg-pool")
            .Select(o => o.DataKind).ToListAsync();

        pools.Should().BeEquivalentTo(["real", "qa"]);
    }

    [Fact]
    public async Task ASecondOfferForTheSameFuelAndPool_IsRefused()
    {
        // The natural key, enforced. Without it two offers for one (station, fuel, pool) would let
        // the storefront's per-litre price become ambiguous again - the defect this whole split is
        // about, re-entering through the new tables.
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        context.FuelOffers.Add(new FuelOffer
        {
            Id = "dup-1",
            StationId = "upg",
            FuelTypeId = "upg-dup",
            DataKind = DataKinds.Real,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.FuelOffers.Add(new FuelOffer
        {
            Id = "dup-2",           // different id, same natural key
            StationId = "upg",
            FuelTypeId = "upg-dup",
            DataKind = DataKinds.Real,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ASameVolumeTwiceForOneOffer_IsRefused()
    {
        // THE guarantee of the split. The ambiguous checkout lookup that #315 had to resolve with an
        // ORDER BY cannot come back through these tables.
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        var offer = new FuelOffer
        {
            Id = "vol-offer",
            StationId = "upg",
            FuelTypeId = "upg-vol",
            DataKind = DataKinds.Real,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.FuelOffers.Add(offer);
        context.FuelOfferVolumes.Add(new FuelOfferVolume
        {
            Id = "vol-a",
            OfferId = offer.Id,
            Liters = 10m,
            PackagePrice = 500m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.FuelOfferVolumes.Add(new FuelOfferVolume
        {
            Id = "vol-b",           // different id, same (offer, litres)
            OfferId = offer.Id,
            Liters = 10m,
            PackagePrice = 400m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "a duplicate volume for one offer is the exact ambiguity this split exists to remove");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task ANonPositiveVolume_IsRefused(decimal liters)
    {
        // Reachable from the admin API: PUT .../nominals binds List<int> off the request body. #314
        // closed this on fuel_packages; the bound has to come with the split or it is simply reopened.
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        var offer = new FuelOffer
        {
            Id = $"neg-offer-{liters}",
            StationId = "upg",
            FuelTypeId = "upg-neg",
            DataKind = DataKinds.Real,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.FuelOffers.Add(offer);
        context.FuelOfferVolumes.Add(new FuelOfferVolume
        {
            Id = $"neg-vol-{liters}",
            OfferId = offer.Id,
            Liters = liters,
            PackagePrice = 100m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ANegativeMargin_IsRefusedOnTheOffer()
    {
        // A negative margin is not a cheap mistake: the dashboard and the reconciliation report both
        // multiply this straight into revenue, so a sign error there is a wrong number on a screen
        // rather than a failed write.
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        context.FuelOffers.Add(new FuelOffer
        {
            Id = "neg-margin-offer",
            StationId = "upg",
            FuelTypeId = "upg-margin",
            DataKind = DataKinds.Real,
            MarginUahPerLiter = -1m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AnUnknownPool_IsRefusedOnTheOffer()
    {
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        context.FuelOffers.Add(new FuelOffer
        {
            Id = "bad-pool-offer",
            StationId = "upg",
            FuelTypeId = "upg-badpool",
            DataKind = "sandbox",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ArchivingAVolume_DoesNotByItselfArchiveTheOffer()
    {
        // A caveat about what the schema does and does not guarantee, written as a test because the
        // opposite of this is a natural assumption and an expensive one.
        //
        // The backfill derives the offer's own flag with bool_and(is_deleted): an offer follows its
        // volumes out of the catalogue exactly when the last one is archived. That rule runs ONCE, in
        // the migration. There is no trigger, so archiving a volume afterwards leaves the offer live.
        //
        // That is deliberate for this PR - nothing writes the new tables yet - and it becomes PR B/C's
        // job to maintain the flag on every archive path (BrandsController's three loops and
        // DeletePackageCommandHandler). A test that asserted "archiving the last volume archives the
        // offer" would pass today only by asserting something false, so it asserts the real behaviour
        // instead, and fails loudly if a trigger is ever added.
        await using var context = CreateContext();

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_offers (id, station_id, fuel_type_id, data_kind, final_price_per_liter,
                                     created_at_utc, updated_at_utc, is_deleted)
            VALUES ('arch-offer', 'upg', 'upg-arch', 'real', 60, now(), now(), false)
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_offer_volumes (id, offer_id, liters, package_price, is_deleted,
                                           created_at_utc, updated_at_utc)
            VALUES ('arch-vol-live', 'arch-offer', 10, 600, false, now(), now()),
                   ('arch-vol-dead', 'arch-offer', 20, 1200, true,  now(), now())
            """);

        // One volume still on sale, so the offer is live - which is what the backfill's bool_and
        // would have produced, and what the current schema still produces.
        var offer = await context.FuelOffers.IgnoreQueryFilters().SingleAsync(o => o.Id == "arch-offer");
        offer.IsDeleted.Should().BeFalse("one volume of this offer is still on sale");

        await context.Database.ExecuteSqlRawAsync(
            """UPDATE fuel_offer_volumes SET is_deleted = true WHERE id = 'arch-vol-live'""");

        context.ChangeTracker.Clear();

        var reloaded = await context.FuelOffers.IgnoreQueryFilters().SingleAsync(o => o.Id == "arch-offer");
        reloaded.IsDeleted.Should().BeFalse(
            "no trigger maintains this; the archive paths in PR C have to. If this ever becomes true, " +
            "PR C's write paths should stop updating the offer flag by hand");
    }

    [Fact]
    public async Task AnArchivedVolume_StaysReadable()
    {
        // The reason volumes carry their own is_deleted rather than inheriting the offer's: an offer
        // keeps selling while one size leaves the catalogue, and an archived volume still has to be
        // readable for the reports that resolve an order line back to what was bought.
        //
        // Its own rows rather than the previous test's, because the whole class shares one database
        // and xUnit does not promise an order - reading a neighbour's rows would make this test
        // depend on which of the two ran first.
        await using var context = CreateContext();

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_offers (id, station_id, fuel_type_id, data_kind, final_price_per_liter,
                                     created_at_utc, updated_at_utc, is_deleted)
            VALUES ('keep-offer', 'upg', 'upg-keep', 'real', 60, now(), now(), false)
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_offer_volumes (id, offer_id, liters, package_price, is_deleted,
                                           created_at_utc, updated_at_utc)
            VALUES ('keep-vol-live', 'keep-offer', 10, 600, false, now(), now()),
                   ('keep-vol-dead', 'keep-offer', 20, 1200, true,  now(), now())
            """);

        var archived = await context.FuelOfferVolumes.IgnoreQueryFilters()
            .CountAsync(v => v.OfferId == "keep-offer" && v.IsDeleted);

        var visible = await context.FuelOfferVolumes
            .CountAsync(v => v.OfferId == "keep-offer");

        archived.Should().Be(1, "the archived volume is still there, just out of the catalogue");

        visible.Should().Be(1, "and it is absent from the default query, without the offer being archived");

        var offer = await context.FuelOffers.IgnoreQueryFilters().SingleAsync(o => o.Id == "keep-offer");
        offer.IsDeleted.Should().BeFalse("one of its two volumes is still on sale");
    }

    [Fact]
    public async Task TheSeedRows_CameThroughWithTheirPrices()
    {
        // A spot check that the backfill actually moved numbers, not just rows. 30 seeded offers from
        // the 32 package rows (two of the fuel types share the 2 L and 3 L extras).
        await using var context = CreateContext();

        var okko95 = await context.FuelOffers.IgnoreQueryFilters()
            .SingleAsync(o => o.StationId == "okko" && o.FuelTypeId == "okko-95" && o.DataKind == DataKinds.Real);

        okko95.FinalPricePerLiter.Should().Be(51m, "SeedData sets DiscountPrice, and the split carries it");
        okko95.SupplierPricePerLiter.Should().Be(49m);
        okko95.MarginUahPerLiter.Should().Be(2m);

        var volumes = await context.FuelOfferVolumes.IgnoreQueryFilters()
            .Where(v => v.OfferId == okko95.Id)
            .Select(v => new { v.Liters, v.PackagePrice })
            .OrderBy(v => v.Liters)
            .ToListAsync();

        volumes.Should().HaveCount(3, "the seed gives okko-95 its 10/20/50 L nominals");
        volumes.Select(v => v.Liters).Should().BeEquivalentTo([10m, 20m, 50m]);
        volumes.Single(v => v.Liters == 10m).PackagePrice.Should().Be(510m, "10 L at 51.00");
    }
}