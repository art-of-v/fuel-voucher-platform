using FluentAssertions;
using FuelFlow.Features.Pricing;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_offer_prices</c> is the append-only record of what each offer was priced at. Two claims are
/// worth a database behind them.
/// <para>
/// <b>The migration opens every history.</b> An offer that existed before the table did has no rows in
/// it, and the first question about any price is "and before that?" - so the backfill has to have run,
/// and the price it wrote has to be the price the offer holds.
/// </para>
/// <para>
/// <b>The newest row is the offer's current price.</b> This is the whole invariant the table rests on,
/// because <c>fuel_offers</c> keeps its own price columns and is what every read path uses. Nothing in
/// the schema enforces it - the two can only be compared by reading both - so it is checked here after
/// the write paths have run, rather than being stated in a comment and trusted.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class OfferPriceHistoryIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OfferPriceHistoryIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>A recorder over the same context, which is how production wires it.</summary>
    private static OfferPriceRecorder Recorder(ApplicationDbContext context) => new(context);

    [Fact]
    public async Task EveryOfferThatExistedBefore_TheTable_HasAnOpeningPriceRow()
    {
        await using var context = CreateContext();

        var withoutHistory = await context.FuelOffers
            .IgnoreQueryFilters()
            .Where(o => !context.FuelOfferPrices.Any(p => p.OfferId == o.Id))
            .Select(o => o.Id)
            .ToListAsync();

        withoutHistory.Should().BeEmpty(
            "the migration writes one row per offer, and every price change since appends another");
    }

    [Fact]
    public async Task TheOpeningRow_CarriesThePricesTheOfferHolds()
    {
        await using var context = CreateContext();

        // Materialised before the comparison, because the customer price is computed by a method EF
        // cannot translate: it is `min(cost + margin, pump - minDiscount)` over four nullable columns,
        // and the whole point of dropping the stored copy was to stop the database holding an answer
        // this function gives.
        var offers = await context.FuelOffers
            .IgnoreQueryFilters()
            .Select(o => new
            {
                o.Id,
                o.SupplierPricePerLiter,
                o.MarginUahPerLiter,
                o.MarginPercent,
                o.PumpPricePerLiter,
                o.MinDiscountPerLiter
            })
            .ToListAsync()
            .ContinueWith(t => t.Result.Select(o => new
            {
                o.Id,
                o.SupplierPricePerLiter,
                o.MarginUahPerLiter,
                o.MarginPercent,
                o.PumpPricePerLiter,
                o.MinDiscountPerLiter,
                ComputedFinal = FuelPricing.FinalPerLiterOrNull(
                    o.SupplierPricePerLiter, o.MarginUahPerLiter, o.PumpPricePerLiter, o.MinDiscountPerLiter)
            }).ToList());

        var rows = await context.FuelOfferPrices.AsNoTracking().ToListAsync();

        var latest = rows
            .GroupBy(p => p.OfferId)
            .ToDictionary(
                g => g.Key,
                // Ascending order plus overwrite-on-equal keeps the newest, which is the same
                // "current" rule the recorder and the endpoint use.
                g => g.OrderBy(p => p.EffectiveFromUtc).ThenBy(p => p.Id).Last());

        var mismatches = offers
            .Where(o => !latest.TryGetValue(o.Id, out var current)
                || o.SupplierPricePerLiter != current.SupplierPricePerLiter
                || o.MarginUahPerLiter != current.MarginUahPerLiter
                || o.MarginPercent != current.MarginPercent
                || o.PumpPricePerLiter != current.PumpPricePerLiter
                || o.MinDiscountPerLiter != current.MinDiscountPerLiter
                || o.ComputedFinal != current.FinalPricePerLiter)
            .Select(o => o.Id)
            .ToList();

        mismatches.Should().BeEmpty(
            "the newest history row is the offer's current price - the invariant FuelOfferPrice states");
    }

    [Fact]
    public async Task AReprice_AppendsARow_AndLeavesTheEarlierOneAlone()
    {
        await using var context = CreateContext();

        var offer = await SeedOfferAsync(context, "history-append", 50m, 2m);
        await Recorder(context).RecordAsync(offer, OfferPriceReasons.Operator, TestUser);
        await context.SaveChangesAsync();
        var before = await RowsAsync(context, offer.Id);
        before.Should().HaveCount(1, "the opening row is what the second one is compared against");

        // A margin lives on the fuel now, so this is the row that changes. The offer's own copy is
        // inert: setting it and recording would produce an identical row and no history, which is the
        // whole subject of this test.
        var fuel = await context.FuelTypes.SingleAsync(f => f.Id == offer.FuelTypeId);
        fuel.MarginUahPerLiter = 3m;   // 50 + 3 = the 53 the row below is expected to hold
        await Recorder(context).RecordAsync(offer, OfferPriceReasons.Operator, TestUser, DateTime.UtcNow);
        await context.SaveChangesAsync();

        var after = await RowsAsync(context, offer.Id);
        after.Should().HaveCount(before.Count + 1);

        // The row that was there before still says what it said. Append-only is the property that makes
        // "what did it cost last month" answerable; an edited row would answer it with today's number.
        before.Single().MarginUahPerLiter.Should().Be(2m);
        after[^1].MarginUahPerLiter.Should().Be(3m);
        after[^1].Reason.Should().Be(OfferPriceReasons.Operator);
        after[^1].ChangedByUserId.Should().Be(TestUser);
        after[^1].EffectiveFromUtc.Should().BeOnOrAfter(after[0].EffectiveFromUtc);

        // And the fuel still holds the new price, which is what every read path uses.
        await context.Entry(fuel).ReloadAsync();
        fuel.MarginUahPerLiter.Should().Be(3m);
    }

    [Fact]
    public async Task AReprice_ToTheSameNumber_AppendsNothing()
    {
        await using var context = CreateContext();

        var offer = await SeedOfferAsync(context, "history-noop", 50m, 2m);
        await Recorder(context).RecordAsync(offer, OfferPriceReasons.Operator, TestUser);
        await context.SaveChangesAsync();

        var before = await RowsAsync(context, offer.Id);

        // The pump import runs unattended over the whole catalogue every quarter hour. A row per fuel
        // per run would bury the changes this table exists to surface.
        var appended = await Recorder(context).RecordAsync(offer, OfferPriceReasons.PumpImport, null);
        await context.SaveChangesAsync();

        appended.Should().BeFalse();
        (await RowsAsync(context, offer.Id)).Should().HaveCount(before.Count);
    }

    [Fact]
    public async Task Recording_StampsTheOffersOwnPriceColumns()
    {
        await using var context = CreateContext();

        var offer = await SeedOfferAsync(context, "history-stamp", 50m, 2m);
        offer.PriceUpdatedAt.Should().BeNull("a freshly seeded offer has never been repriced");

        var at = DateTime.UtcNow;
        await Recorder(context).RecordAsync(offer, OfferPriceReasons.Operator, TestUser, at);
        await context.SaveChangesAsync();

        // Two of the five write paths used to leave these alone, which is why the audit trail could not
        // say when a fuel was repriced. The choke point cannot be forgotten now.
        offer.PriceUpdatedAt.Should().NotBeNull();
        offer.PriceUpdatedByUserId.Should().Be(TestUser);
        offer.PriceUpdatedAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Recording_ForAnUnattendedImport_LeavesNoActor()
    {
        await using var context = CreateContext();

        var offer = await SeedOfferAsync(context, "history-unattended", 50m, 2m);
        await Recorder(context).RecordAsync(offer, OfferPriceReasons.PumpImport, null);
        await context.SaveChangesAsync();

        offer.PriceUpdatedByUserId.Should().BeNull();
        (await RowsAsync(context, offer.Id)).Single().ChangedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task AHistoryRow_CannotBeWrittenForAnOfferThatDoesNotExist()
    {
        await using var context = CreateContext();

        var act = async () =>
        {
            // The FK is only enforced on write: AddAsync stages the row, SaveChanges is the statement
            // the database refuses.
            await context.FuelOfferPrices.AddAsync(new FuelOfferPrice
            {
                Id = Guid.CreateVersion7(),
                OfferId = "no-such-offer",
                StationId = "okko",
                FuelTypeId = "no-such-fuel",
                EffectiveFromUtc = DateTime.UtcNow,
                RecordedAtUtc = DateTime.UtcNow,
                Reason = OfferPriceReasons.Operator
            });
            await context.SaveChangesAsync();
        };

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AnUnknownReason_IsRefusedByTheDatabase()
    {
        await using var context = CreateContext();

        var offer = await SeedOfferAsync(context, "history-bad-reason", 50m, 2m);
        context.FuelOfferPrices.Add(new FuelOfferPrice
        {
            Id = Guid.CreateVersion7(),
            OfferId = offer.Id,
            StationId = offer.StationId,
            FuelTypeId = offer.FuelTypeId,
            DataKind = offer.DataKind,
            EffectiveFromUtc = DateTime.UtcNow,
            RecordedAtUtc = DateTime.UtcNow,
            Reason = "someone-typed-a-new-one"
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AnOffer_CannotBeDeleted_WhileItsPricesAreRecorded()
    {
        await using var context = CreateContext();

        var offer = await SeedOfferAsync(context, "history-restrict", 50m, 2m);
        await Recorder(context).RecordAsync(offer, OfferPriceReasons.Operator, TestUser);
        await context.SaveChangesAsync();

        var volume = new FuelOfferVolume
        {
            Id = $"vol-history-restrict-{Guid.CreateVersion7()}",
            OfferId = offer.Id,
            Liters = 10m,
            PriceOverrideUah = 520m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.FuelOfferVolumes.Add(volume);
        await context.SaveChangesAsync();

        context.FuelOfferVolumes.Remove(volume);
        await context.SaveChangesAsync();
        context.FuelOffers.Remove(offer);

        // RESTRICT, like every other relation in this schema: a price is a record of something sold, so
        // the offer it was sold under has to stay readable. Removing the rehearsal pool removes its
        // history first - see scripts/remove-qa-catalogue.sql.
        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static readonly Guid TestUser = Guid.CreateVersion7();

    private static async Task<FuelOffer> SeedOfferAsync(
        ApplicationDbContext context,
        string suffix,
        decimal supplierPrice,
        decimal margin)
    {
        var fuelId = $"fuel-{suffix}-{Guid.CreateVersion7()}";

        // The offer and the fuel it sells. Since 20261018104336 an offer may not name a fuel that
        // does not exist, and these ids are generated per call, so the fuel has to be born with the
        // offer rather than seeded from a list that would go stale the moment a suffix changed.
        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelId,
            Name = $"Test fuel {suffix}",
            StationId = "okko",
            BasePrice = 0m,
            DiscountPrice = 0m,
            MarginUahPerLiter = margin,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var offer = new FuelOffer
        {
            Id = fuelId,
            StationId = "okko",
            FuelTypeId = fuelId,
            DataKind = DataKinds.Real,
            SupplierPricePerLiter = supplierPrice,
            MarginUahPerLiter = margin,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.FuelOffers.Add(offer);
        await context.SaveChangesAsync();
        return offer;
    }

    private static async Task<List<FuelOfferPrice>> RowsAsync(
        ApplicationDbContext context,
        string offerId) =>
        await context.FuelOfferPrices
            .AsNoTracking()
            .Where(p => p.OfferId == offerId)
            .OrderBy(p => p.EffectiveFromUtc)
            .ThenBy(p => p.Id)
            .Select(p => new FuelOfferPrice
            {
                Id = p.Id,
                OfferId = p.OfferId,
                SupplierPricePerLiter = p.SupplierPricePerLiter,
                MarginUahPerLiter = p.MarginUahPerLiter,
                MarginPercent = p.MarginPercent,
                FinalPricePerLiter = p.FinalPricePerLiter,
                PumpPricePerLiter = p.PumpPricePerLiter,
                MinDiscountPerLiter = p.MinDiscountPerLiter,
                EffectiveFromUtc = p.EffectiveFromUtc,
                RecordedAtUtc = p.RecordedAtUtc,
                ChangedByUserId = p.ChangedByUserId,
                Reason = p.Reason
            })
            .ToListAsync();
}
