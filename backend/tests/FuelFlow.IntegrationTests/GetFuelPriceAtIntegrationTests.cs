using FluentAssertions;
using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// "What was this fuel priced at, then" - the question an order line from last month asks, and the one
/// the series endpoint cannot answer once the moment falls outside its newest 200 rows.
/// <para>
/// Two things are being pinned here, and both are the kind that only a database can prove. The
/// <b>rule</b>: the price in effect at T is the greatest <c>effective_from</c> at or before T, and where
/// two rows share a timestamp the greater id wins - the same rule the history endpoint and the table's
/// own documentation state for "current". Two definitions of "the price in effect" would disagree
/// exactly when it matters, which is on a row somebody is about to settle money against. The
/// <b>boundaries</b>: a moment before the fuel's first price, a moment far in the future, a backdated
/// row, and two reprices inside one transaction.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class GetFuelPriceAtIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public GetFuelPriceAtIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// A fuel and one offer of its own, so a test's rows are the only ones the query can find. The ids
    /// are suffixed rather than random so two tests in this class cannot collide on them.
    /// </summary>
    private async Task<(string FuelTypeId, string OfferId)> SeedFuelAsync(
        ApplicationDbContext context,
        string suffix)
    {
        var station = $"at-{suffix}";
        var fuelTypeId = $"at-fuel-{suffix}";
        var offerId = $"at-offer-{suffix}";

        var now = DateTime.UtcNow;
        context.Stations.Add(new Station
        {
            Id = station, Name = $"AT {suffix}", Color = "#000", LogoText = "AT",
        });
        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelTypeId,
            Name = $"AT {suffix}",
            StationId = station,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        context.FuelOffers.Add(new FuelOffer
        {
            Id = offerId,
            StationId = station,
            FuelTypeId = fuelTypeId,
            DataKind = DataKinds.Real,
            PumpPricePerLiter = 50m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();
        return (fuelTypeId, offerId);
    }

    private static FuelOfferPrice Row(
        string offerId,
        string fuelTypeId,
        DateTime from,
        decimal? pump,
        string reason = OfferPriceReasons.Operator) => new()
        {
            Id = Guid.CreateVersion7(),
            OfferId = offerId,
            StationId = "at-x",
            FuelTypeId = fuelTypeId,
            DataKind = DataKinds.Real,
            PumpPricePerLiter = pump,
            FinalPricePerLiter = pump,
            EffectiveFromUtc = from,
            RecordedAtUtc = DateTime.UtcNow,
            Reason = reason,
        };

    [Fact]
    public async Task ThePriceInEffectAtAMoment_IsTheOneThatFollowedIt()
    {
        await using var context = CreateContext();
        var handler = new GetFuelPriceAtQueryHandler(context);
        var (fuelTypeId, offerId) = await SeedFuelAsync(context, "follow");

        var t1 = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 3, 9, 8, 0, 0, DateTimeKind.Utc);

        context.FuelOfferPrices.AddRange(
            Row(offerId, fuelTypeId, t1, 50m),
            Row(offerId, fuelTypeId, t2, 51m),
            Row(offerId, fuelTypeId, t3, 49m));
        await context.SaveChangesAsync();

        (await handler.HandleAsync(new GetFuelPriceAtQuery(fuelTypeId, t1.AddSeconds(1))))
            .PumpPricePerLiter.Should().Be(50m);
        (await handler.HandleAsync(new GetFuelPriceAtQuery(fuelTypeId, t2)))
            .PumpPricePerLiter.Should().Be(51m, "a row is in effect from the instant it took effect, inclusive");
        (await handler.HandleAsync(new GetFuelPriceAtQuery(fuelTypeId, t3.AddDays(30))))
            .PumpPricePerLiter.Should().Be(49m, "and keeps standing until the next one takes over");
    }

    [Fact]
    public async Task AMomentBeforeTheFirstPrice_HadNoPriceAtAll()
    {
        await using var context = CreateContext();
        var handler = new GetFuelPriceAtQueryHandler(context);
        var (fuelTypeId, offerId) = await SeedFuelAsync(context, "before");

        var firstPrice = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        context.FuelOfferPrices.Add(Row(offerId, fuelTypeId, firstPrice, 50m));
        await context.SaveChangesAsync();

        var answer = await handler.HandleAsync(new GetFuelPriceAtQuery(
            fuelTypeId, firstPrice.AddTicks(-1)));

        answer.Should().BeNull(
            "a fuel that was not priced yet has no price in effect, which is an answer and not a failure");
    }

    [Fact]
    public async Task AMomentFarInTheFuture_GetsTheCurrentPrice()
    {
        await using var context = CreateContext();
        var handler = new GetFuelPriceAtQueryHandler(context);
        var (fuelTypeId, offerId) = await SeedFuelAsync(context, "future");

        context.FuelOfferPrices.Add(Row(offerId, fuelTypeId, new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc), 50m));
        await context.SaveChangesAsync();

        var answer = await handler.HandleAsync(new GetFuelPriceAtQuery(fuelTypeId, DateTime.UtcNow.AddYears(5)));

        answer!.PumpPricePerLiter.Should().Be(50m);
        answer.SupersededAtUtc.Should().BeNull("nothing displaced it, so it is the price in effect now");
    }

    [Fact]
    public async Task APriceEnteredTodayForLastTuesday_IsThePriceThatStoodLastTuesday()
    {
        // Backdated entry is the whole reason effective_from exists and recorded_at does not: a price
        // corrected this morning for last Tuesday is the price that stood last Tuesday, and reading by
        // write time would hide it from the one question it was entered to answer.
        await using var context = CreateContext();
        var handler = new GetFuelPriceAtQueryHandler(context);
        var (fuelTypeId, offerId) = await SeedFuelAsync(context, "backdated");

        var lastTuesday = new DateTime(2026, 3, 3, 8, 0, 0, DateTimeKind.Utc);
        var backdated = Row(offerId, fuelTypeId, lastTuesday, 47.25m);
        backdated.RecordedAtUtc = DateTime.UtcNow;
        context.FuelOfferPrices.Add(backdated);
        await context.SaveChangesAsync();

        var answer = await handler.HandleAsync(new GetFuelPriceAtQuery(
            fuelTypeId, lastTuesday.AddHours(2)));

        answer!.PumpPricePerLiter.Should().Be(
            47.25m,
            "the column is effective_from, so a row written today about last Tuesday is still found");
    }

    [Fact]
    public async Task TwoRepricesInOneInstant_ResolveToTheLaterOne()
    {
        // Two rows can share effective_from: there is deliberately no unique index on (offer, from),
        // because refusing a second reprice in the same millisecond would trade a cosmetic ambiguity
        // for a write that can fail. The tie-break is the greater id, which is a version 7 UUID and so
        // breaks the tie chronologically rather than arbitrarily.
        await using var context = CreateContext();
        var handler = new GetFuelPriceAtQueryHandler(context);
        var (fuelTypeId, offerId) = await SeedFuelAsync(context, "tie");

        var at = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        var first = Row(offerId, fuelTypeId, at, 50m);
        var second = Row(offerId, fuelTypeId, at, 53m);
        context.FuelOfferPrices.AddRange(first, second);
        await context.SaveChangesAsync();

        var winner = await handler.HandleAsync(new GetFuelPriceAtQuery(fuelTypeId, at.AddMinutes(1)));

        var expected = first.Id > second.Id ? 50m : 53m;
        winner!.PumpPricePerLiter.Should().Be(expected, "the greater id wins, exactly as 'current' does");
    }

    [Fact]
    public async Task TheAnswerSaysHowLongThePriceStood()
    {
        // "What did it cost then" is half a question without "and until when": an operator comparing two
        // order lines needs to know whether they were both inside one price window or across a change.
        await using var context = CreateContext();
        var handler = new GetFuelPriceAtQueryHandler(context);
        var (fuelTypeId, offerId) = await SeedFuelAsync(context, "window");

        var t1 = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc);
        context.FuelOfferPrices.AddRange(
            Row(offerId, fuelTypeId, t1, 50m),
            Row(offerId, fuelTypeId, t2, 51m));
        await context.SaveChangesAsync();

        var answer = await handler.HandleAsync(new GetFuelPriceAtQuery(fuelTypeId, t2.AddHours(-1)));

        answer!.EffectiveFromUtc.Should().Be(t1);
        answer.SupersededAtUtc.Should().Be(t2, "derived from the next row rather than stored twice");
    }

    [Fact]
    public async Task AFuelWithNoHistoryAtAll_ReturnsNothing()
    {
        await using var context = CreateContext();
        var handler = new GetFuelPriceAtQueryHandler(context);
        var (fuelTypeId, _) = await SeedFuelAsync(context, "empty");

        var answer = await handler.HandleAsync(new GetFuelPriceAtQuery(fuelTypeId, DateTime.UtcNow));

        answer.Should().BeNull();
    }

    [Fact]
    public async Task TheAnswerAgreesWithTheHistoryEndpoint_AboutTheSameMoment()
    {
        // Two definitions of "the price in effect" would drift, and the drift would show up on an order
        // line rather than in a test. Read the series and this query at the same instants and they must
        // name the same row.
        await using var context = CreateContext();
        var atHandler = new GetFuelPriceAtQueryHandler(context);
        var historyHandler = new FuelFlow.Features.Brands.GetFuelPriceHistory.GetFuelPriceHistoryQueryHandler(context);
        var (fuelTypeId, offerId) = await SeedFuelAsync(context, "agree");

        var t1 = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 3, 9, 8, 0, 0, DateTimeKind.Utc);
        context.FuelOfferPrices.AddRange(
            Row(offerId, fuelTypeId, t1, 50m),
            Row(offerId, fuelTypeId, t2, 51m),
            Row(offerId, fuelTypeId, t3, 49m));
        await context.SaveChangesAsync();

        foreach (var moment in new[] { t1, t2.AddHours(3), t3.AddDays(1) })
        {
            var single = await atHandler.HandleAsync(new GetFuelPriceAtQuery(fuelTypeId, moment));
            var series = await historyHandler.HandleAsync(
                new FuelFlow.Features.Brands.GetFuelPriceHistory.GetFuelPriceHistoryQuery(fuelTypeId));

            var standing = series.Last(p => p.EffectiveFromUtc <= moment);

            single!.Id.Should().Be(
                standing.Id,
                "the single-row answer must be the same row the series ends on at that moment");
        }
    }
}
