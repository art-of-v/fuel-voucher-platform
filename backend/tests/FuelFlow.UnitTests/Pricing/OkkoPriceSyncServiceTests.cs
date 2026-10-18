using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.BackgroundJobs;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Pricing.BackfillBrandFuelCodes;
using FuelFlow.Features.Pricing.ImportOkkoCatalogue;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// The scheduled pump-price refresh. What matters here is not the arithmetic (covered by
/// <see cref="ImportOkkoPumpPricesTests"/>) but the unattended failure modes: a fetch that fails must
/// not move a price, and a disabled switch must not reach the network at all.
/// </summary>
public sealed class OkkoPriceSyncServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public OkkoPriceSyncServiceTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options, TestQrCipher.Instance);
        _events = new AuditEventService(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>A client stub that either answers or blows up, and records whether it was asked.</summary>
    private sealed class StubClient : IOkkoPriceClient
    {
        private readonly Func<IReadOnlyList<OkkoPumpPrice>> _respond;
        public int Calls { get; private set; }

        public StubClient(Func<IReadOnlyList<OkkoPumpPrice>> respond) => _respond = respond;

        public Task<IReadOnlyList<OkkoPumpPrice>> FetchAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_respond());
        }
    }

    private static readonly OkkoPumpPrice[] Sheet =
    [
        new("A-95", 92.90m),
        new("DP", 99.90m),
        new("SPBT", 47.90m),
        new("Pulls 95", 95.90m),
        new("Pulls Diesel", 102.90m),
    ];

    private OkkoPriceSyncService Service(StubClient client, bool enabled, ILogger<OkkoPriceSyncService>? logger = null)
        => new(
            client,
            new ImportOkkoPumpPricesCommandHandler(_context, _events, new OfferPriceRecorder(_context)),
            new ImportOkkoCatalogueCommandHandler(_context, _events, new OfferPriceRecorder(_context)),
            new BackfillBrandFuelCodesCommandHandler(_context, _events),
            _context,
            Options.Create(new OkkoPriceOptions { Enabled = enabled }),
            logger ?? NullLogger<OkkoPriceSyncService>.Instance);

    /// <summary>The production seed shape: five OKKO fuels, each with a cost/margin/pump already set.</summary>
    private async Task SeedAsync(decimal pump = 50m)
    {
        _context.Brands.Add(new Brand { Id = "okko", Name = "OKKO", Color = "#000", LogoText = "OKKO" });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", BrandId = "okko" },
            new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", BrandId = "okko" },
            new FuelTypeEntity { Id = "okko-gas", Name = "ГАЗ", BrandId = "okko" },
            new FuelTypeEntity { Id = "okko-p95", Name = "Pulls 95", BrandId = "okko" },
            new FuelTypeEntity { Id = "okko-pulls-dp", Name = "ДП PULLS", BrandId = "okko" });

        foreach (var (fuelTypeId, fuelName) in new[]
                 {
                     ("okko-95", "A-95"), ("okko-dp", "ДП ЄВРО"), ("okko-gas", "ГАЗ"),
                     ("okko-p95", "Pulls 95"), ("okko-pulls-dp", "ДП PULLS"),
                 })
        {
            foreach (var liters in new[] { 10m, 50m })
            {
                _context.SeedPackage(
                    id: $"{fuelTypeId}-{liters}",
                    BrandId: "okko",
                    fuelTypeId: fuelTypeId,
                    liters: liters,
                    price: Math.Round((40m + 3m) * liters, 2, MidpointRounding.AwayFromZero),
                    supplierPricePerLiter: 40m,
                    marginUahPerLiter: 3m,
                    pumpPricePerLiter: pump,
                    minDiscountPerLiter: 1m);
            }
        }

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Sync_ShouldApplyFetchedPrices_ToEveryCatalogFuel()
    {
        await SeedAsync(pump: 50m);

        await Service(new StubClient(() => Sheet), enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        var gas = await OfferOfAsync("okko-gas-50");
        gas.PumpPricePerLiter.Should().Be(47.90m);

        var diesel = await OfferOfAsync("okko-dp-50");
        diesel.PumpPricePerLiter.Should().Be(99.90m);
    }

    [Fact]
    public async Task Sync_ShouldNotReachTheNetwork_WhenDisabled()
    {
        await SeedAsync();
        var client = new StubClient(() => Sheet);

        await Service(client, enabled: false).SyncPricesAsync();

        // A fresh deployment must be able to sit with the switch off without polling OKKO at all.
        client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Sync_ShouldKeepTheLastKnownPrices_WhenTheFetchFails()
    {
        await SeedAsync(pump: 50m);
        var client = new StubClient(() => throw new OkkoPriceScrapeException("HTTP 503"));

        await Service(client, enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        // The important one: an outage must leave yesterday's prices intact, not blank them. Blanking
        // would flip pricing back to cost-plus and hide the "до" price from customers.
        var pkg = await OfferOfAsync("okko-95-50");
        pkg.PumpPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task Sync_ShouldKeepTheLastKnownPrices_WhenTheFetchThrowsAnythingElse()
    {
        await SeedAsync(pump: 50m);
        var client = new StubClient(() => throw new HttpRequestException("connection reset"));

        await Service(client, enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        (await OfferOfAsync("okko-95-50"))            .PumpPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task Sync_ShouldRefuseAPumpThatWouldSellBelowCost_AndSaySo()
    {
        // A-95 costs 40 + 3 margin = 43. A pump of 41 gives a ceiling of 40, which is at cost and
        // below the moment minDiscount pushes under it — and a below-cost fuel is refused at checkout,
        // so accepting this automatically would silently take the fuel off sale.
        await SeedAsync(pump: 50m);
        var cheap = new[] { new OkkoPumpPrice("A-95", 40.50m) };

        var result = await new ImportOkkoPumpPricesCommandHandler(_context, _events, new OfferPriceRecorder(_context))
            .ApplyAsync(cheap, dryRun: false, Guid.Empty, "OKKO price sync");

        result.BelowCostSkipped.Should().ContainSingle().Which.Should().Be("A-95");
        result.FuelsChanged.Should().Be(0);
        _context.ChangeTracker.Clear();
        (await OfferOfAsync("okko-95-50"))            .PumpPricePerLiter.Should().Be(50m);
    }

    // The refusal is a guard doing its job, not a fault - and this runs every 15 minutes. At Error it
    // filled the admin error journal with the same refusal until a real failure could not be found
    // in it; the journal only keeps Error and above, so the level is what keeps it readable.
    [Fact]
    public async Task Sync_ShouldReportABelowCostRefusalAsAWarning_NotAnError()
    {
        await SeedAsync(pump: 50m);
        var logger = new CapturingLogger<OkkoPriceSyncService>();

        // A-95 costs 40 + 3 margin = 43; a pump of 40.50 puts the ceiling under cost.
        await Service(new StubClient(() => [new OkkoPumpPrice("A-95", 40.50m)]), enabled: true, logger)
            .SyncPricesAsync();

        var refusal = logger.Entries.Should().ContainSingle(e => e.Message.Contains("below cost")).Which;
        refusal.Level.Should().Be(LogLevel.Warning);
        refusal.Message.Should().Contain("A-95");
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Sync_ShouldApplyAPumpBelowCost_WhenTheFuelIsOptedIn()
    {
        // AllowBelowCost is the same escape hatch the manual price panel offers: a deliberate
        // loss-leader, entered by a person, must not be second-guessed by the scheduler.
        await SeedAsync(pump: 50m);
        var a95 = await _context.FuelTypes.FindAsync("okko-95");
        a95!.AllowBelowCost = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await new ImportOkkoPumpPricesCommandHandler(_context, _events, new OfferPriceRecorder(_context))
            .ApplyAsync([new OkkoPumpPrice("A-95", 40.50m)], dryRun: false, Guid.Empty, "OKKO price sync");

        result.BelowCostSkipped.Should().BeEmpty();
        result.FuelsChanged.Should().Be(1);
        _context.ChangeTracker.Clear();
        (await OfferOfAsync("okko-95-50"))            .PumpPricePerLiter.Should().Be(40.50m);
    }

    [Fact]
    public async Task Sync_ShouldStillApplyTheOtherFuels_WhenOneIsRefusedAsBelowCost()
    {
        await SeedAsync(pump: 50m);
        var mixed = new[]
        {
            new OkkoPumpPrice("A-95", 40.50m),  // refused
            new OkkoPumpPrice("SPBT", 47.90m),  // fine
        };

        var result = await new ImportOkkoPumpPricesCommandHandler(_context, _events, new OfferPriceRecorder(_context))
            .ApplyAsync(mixed, dryRun: false, Guid.Empty, "OKKO price sync");

        result.BelowCostSkipped.Should().ContainSingle();
        result.FuelsChanged.Should().Be(1);
        _context.ChangeTracker.Clear();
        (await OfferOfAsync("okko-gas-50"))            .PumpPricePerLiter.Should().Be(47.90m);
    }

    [Fact]
    public async Task Sync_ShouldAddAGradeTheCatalogueLacks_BeforeItPricesAnything()
    {
        // The ordering is the contract: a fuel that does not exist yet has nowhere to hold a pump
        // price, so the catalogue half runs first and the pricing half then sees the new fuel. One pass
        // has to leave the catalogue complete AND priced, or a grade sits unpriced until the next run.
        _context.Brands.Add(new Brand { Id = "okko", Name = "OKKO", Color = "#000", LogoText = "OKKO" });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-95", Name = "A-95", BrandId = "okko", BrandFuelCode = "A-95",
            BasePrice = 54, DiscountPrice = 51
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var sheet = new[]
        {
            new OkkoPumpPrice("A-95", 92.90m, "А-95"),   // known already
            new OkkoPumpPrice("DP", 99.90m, "ДП ЄВРО")  // new: the catalogue has no diesel
        };

        await Service(new StubClient(() => sheet), enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        var created = await _context.FuelTypes.SingleAsync(f => f.BrandFuelCode == "DP");
        created.Name.Should().Be(
            "ДП",
            "the name comes from the grade the code identifies; the site's own label is a rendering of it");

        // The new fuel's offer carries the published pump and nothing else, and stays off sale: no
        // volumes, no cost, no margin. It waits for an operator rather than going live at zero.
        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == created.Id);
        offer.PumpPricePerLiter.Should().Be(99.90m);
        offer.SupplierPricePerLiter.Should().BeNull();
        offer.MarginUahPerLiter.Should().BeNull();
        (await _context.FuelOfferVolumes.CountAsync(v => v.OfferId == offer.Id)).Should().Be(0);

        // And the fuel that already existed was priced, not recreated.
        (await _context.FuelTypes.CountAsync(f => f.BrandId == "okko")).Should().Be(2);
    }

    [Fact]
    public async Task Sync_ShouldNotCreateAnything_WhenTheFetchFails()
    {
        // The failure policy the price side already had, extended to the catalogue: a fetch that fails
        // must not be able to change what we sell, and must not be able to empty the catalogue either.
        await SeedAsync(pump: 50m);
        var before = await _context.FuelTypes.CountAsync();

        var service = Service(
            new StubClient(() => throw new OkkoPriceScrapeException("layout changed")), enabled: true);

        await service.SyncPricesAsync();

        (await _context.FuelTypes.CountAsync()).Should().Be(before, "a fetch that failed must not change the catalogue");
    }

    [Fact]
    public async Task Sync_ShouldHoldSupplierCost_AndOnlyMoveTheCeiling()
    {
        await SeedAsync(pump: 50m);

        await Service(new StubClient(() => Sheet), enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        // A pump change is not a purchase-cost event.
        var pkg = await OfferOfAsync("okko-95-50");
        pkg.SupplierPricePerLiter.Should().Be(40m);
        // The margin is the fuel's now. Asserting it on the offer would be asserting a column nothing
        // writes, and it would pass for a reason the production code no longer relies on.
        (await MarginFor(pkg)).Should().Be(3m);
    }

    [Fact]
    public async Task Sync_ShouldAuditEveryPriceItChanges_Unattended()
    {
        await SeedAsync(pump: 50m);

        await Service(new StubClient(() => Sheet), enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        // Nobody typed these numbers, so the audit trail is the only way to answer "who moved this
        // price and when" — it must name the scheduler rather than leave the actor blank.
        var events = await _context.AuditEventOutbox.AsNoTracking()
            .Where(e => e.EventType == "PumpPriceImported")
            .ToListAsync();

        events.Should().HaveCount(5);
        events.Should().OnlyContain(e => e.ChangedByUserName == OkkoPriceSyncService.SyncActorName);
        events.Should().OnlyContain(e => e.Summary!.Contains("imported from site"));
    }

    [Fact]
    public async Task Sync_ShouldBeIdempotent_WhenThePriceHasNotMoved()
    {
        await SeedAsync(pump: 50m);
        var service = Service(new StubClient(() => Sheet), enabled: true);
        await service.SyncPricesAsync();
        _context.ChangeTracker.Clear();

        // What the first pass wrote, by type: five pump rows (the pump moved) and five backfill rows
        // (this fixture's fuels carry no code, which is the state production is in today).
        var afterFirst = await _context.AuditEventOutbox.AsNoTracking()
            .GroupBy(e => e.EventType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var second = Service(new StubClient(() => Sheet), enabled: true);
        await second.SyncPricesAsync();
        _context.ChangeTracker.Clear();

        // No new audit rows on the second pass: the brand history must not fill with daily
        // no-ops that say "nothing changed" every morning. The backfill is in this comparison because it
        // runs in the same job — after pass one there is nothing left uncoded, so it must be silent too.
        var afterSecond = await _context.AuditEventOutbox.AsNoTracking()
            .GroupBy(e => e.EventType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        afterSecond.Should().BeEquivalentTo(afterFirst,
            "a second pass with the same sheet changes nothing and must say nothing");
    }
    /// <summary>
    /// The offer behind a seeded package. Every figure these tests assert on - pump, cost, margin - is
    /// an offer's, and the volume it hangs off is the only thing that identifies "the package".
    /// </summary>
    private Task<FuelOffer> OfferOfAsync(string volumeId)
        => _context.FuelOfferVolumes.AsNoTracking()
            .Where(v => v.Id == volumeId)
            .Select(v => v.Offer)
            .FirstAsync();

    /// <summary>
    /// The margin an offer sells at, which is its fuel's - the same place production reads it since
    /// the offer stopped carrying a copy. A test that asserted the offer's own column would have been
    /// asserting a number nothing writes.
    /// </summary>
    private async Task<decimal?> MarginFor(FuelOffer offer)
        => await _context.FuelTypes
            .Where(f => f.Id == offer.FuelTypeId)
            .Select(f => f.MarginUahPerLiter)
            .FirstOrDefaultAsync();
}
