using FuelFlow.Brands.UnitTests;
using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.Providers.UnitTests;

/// <summary>
/// The read side of <c>fuel_offer_prices</c>. The write side is pinned by
/// <c>OfferPriceHistoryIntegrationTests</c> and by the controller tests; what is left to answer here is
/// the shape an operator gets - which is what a screen or an export will be built against.
/// </summary>
public sealed class GetFuelPriceHistoryQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetFuelPriceHistoryQueryHandler _handler;

    public GetFuelPriceHistoryQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.Database.EnsureCreated();
        _handler = new GetFuelPriceHistoryQueryHandler(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task ItReturnsTheRowsOldestFirst()
    {
        await SeedAsync(fuelTypeId: "fuel-a", at: 10, margin: 2m);
        await SeedAsync(fuelTypeId: "fuel-a", at: 20, margin: 3m);
        await SeedAsync(fuelTypeId: "fuel-a", at: 30, margin: 4m);

        var history = await _handler.HandleAsync(new GetFuelPriceHistoryQuery("fuel-a"));

        // Oldest first, so the current price is the last element and the sequence can be read in order.
        // The reverse would put today's price at the bottom.
        history.Select(h => h.MarginUahPerLiter).Should().Equal(2m, 3m, 4m);
        history.Should().BeInAscendingOrder(h => h.EffectiveFromUtc);
    }

    [Fact]
    public async Task ItCarriesThePoolSoARehearsalPriceIsNeverReadAsALiveOne()
    {
        await SeedAsync(fuelTypeId: "fuel-b", at: 10, margin: 2m, dataKind: DataKinds.Real);
        await SeedAsync(fuelTypeId: "fuel-b", at: 11, margin: 99m, dataKind: DataKinds.Qa);

        var history = await _handler.HandleAsync(new GetFuelPriceHistoryQuery("fuel-b"));

        history.Select(h => h.DataKind).Should().Equal(DataKinds.Real, DataKinds.Qa);
        // Both rows now belong to the same fuel - there is one price per grade - so the pool is the
        // only thing that tells them apart, and it is what the audit trail shows the operator.
        history.Should().HaveCount(2);
        history.Should().OnlyContain(h => h.FuelTypeId == "fuel-b");
        history.Single(h => h.DataKind == DataKinds.Qa).MarginUahPerLiter.Should().Be(99m);
    }

    [Fact]
    public async Task ItReadsOnlyTheFuelAskedFor()
    {
        await SeedAsync(fuelTypeId: "fuel-c", at: 10, margin: 2m);
        await SeedAsync(fuelTypeId: "fuel-d", at: 10, margin: 5m);

        var history = await _handler.HandleAsync(new GetFuelPriceHistoryQuery("fuel-c"));

        history.Should().ContainSingle().Which.FuelTypeId.Should().Be("fuel-c");
    }

    [Fact]
    public async Task TheWindowTakesTheNewestRows_NotTheOldest()
    {
        for (var i = 0; i < 10; i++)
        {
            await SeedAsync(fuelTypeId: "fuel-e", at: 10 + i, margin: i);
        }

        var history = await _handler.HandleAsync(new GetFuelPriceHistoryQuery("fuel-e", Limit: 3));

        // A limit that kept the oldest rows would answer "what did it cost at the start", which is the
        // one question a caller who did not ask for it cannot tell they got wrong.
        history.Select(h => h.MarginUahPerLiter).Should().Equal(7m, 8m, 9m);
    }

    [Fact]
    public async Task AFuelWithNoHistory_ComesBackEmptyRatherThanThrowing()
    {
        var history = await _handler.HandleAsync(new GetFuelPriceHistoryQuery("never-seen"));

        history.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryRecordedFigureIsReturned()
    {
        await SeedAsync(fuelTypeId: "fuel-f", at: 10, margin: 2m);

        var entry = (await _handler.HandleAsync(new GetFuelPriceHistoryQuery("fuel-f"))).Single();

        entry.SupplierPricePerLiter.Should().Be(50m);
        entry.MarginUahPerLiter.Should().Be(2m);
        entry.FinalPricePerLiter.Should().Be(52m);
        entry.PumpPricePerLiter.Should().Be(55m);
        entry.MinDiscountPerLiter.Should().Be(1m);
        entry.Reason.Should().Be(OfferPriceReasons.Operator);
    }

    private async Task SeedAsync(string fuelTypeId, int at, decimal margin, string? dataKind = null)
    {
        _context.FuelOfferPrices.Add(new FuelOfferPrice
        {
            Id = Guid.CreateVersion7(),
            OfferId = offerId,
            BrandId = BrandIds.Okko,
            DataKind = dataKind ?? DataKinds.Real,
            SupplierPricePerLiter = 50m,
            MarginUahPerLiter = margin,
            FinalPricePerLiter = 50m + margin,
            PumpPricePerLiter = 55m,
            MinDiscountPerLiter = 1m,
            EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(at),
            RecordedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(at),
            Reason = OfferPriceReasons.Operator
        });

        await _context.SaveChangesAsync();
    }
}
