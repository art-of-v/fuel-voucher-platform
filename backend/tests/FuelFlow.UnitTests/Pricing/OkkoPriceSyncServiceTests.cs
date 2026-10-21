using FuelFlow.BackgroundJobs;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.BackfillBrandFuelCodes;
using FuelFlow.Features.Pricing.ImportOkkoCatalogue;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FluentAssertions;
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
/// <remarks>
/// <para>
/// <b>These tests ran once.</b> The file was written to disk without a newline in it, so the first
/// <c>//</c> in it commented out the rest — class, tests and all — and the suite stayed green for
/// three weeks while none of this was checked. The shape of the file is now guarded
/// (<c>TestFileShapeTests</c>), and the fixtures speak the model the price actually lives in: the
/// price is on the FUEL, the sizes are <c>fuel_nominals</c> rows, and the pool is on the size.
/// </para>
/// </remarks>
public sealed class OkkoPriceSyncServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    /// <summary>Stable ids per fixture name, so a test can name a fuel it seeded.</summary>
    private readonly Dictionary<string, Guid> _ids = new(StringComparer.Ordinal);

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
            new ImportOkkoPumpPricesCommandHandler(_context, new BrandSlugResolver(_context), _events, new PriceHistoryRecorder(_context)),
            new ImportOkkoCatalogueCommandHandler(_context, new BrandSlugResolver(_context), _events, new PriceHistoryRecorder(_context)),
            new BackfillBrandFuelCodesCommandHandler(_context, new BrandSlugResolver(_context), _events),
            _context,
            Options.Create(new OkkoPriceOptions { Enabled = enabled }),
            logger ?? NullLogger<OkkoPriceSyncService>.Instance);

    /// <summary>The production seed shape: five OKKO fuels, each with a cost/margin/pump already set.</summary>
    private async Task SeedAsync(decimal pump = 50m)
    {
        _context.AddMissing(BrandIds.Okko);

        foreach (var (key, name) in new[]
                 {
                     ("okko-95", "A-95"), ("okko-dp", "ДП ЄВРО"), ("okko-gas", "Газ"),
                     ("okko-p95", "Pulls 95"), ("okko-pulls-dp", "ДП PULLS"),
                 })
        {
            var fuelId = Id(key);

            // The price is the fuel's: one price per litre per grade, whatever the size.
            _context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = fuelId,
                Name = name,
                BrandId = BrandIds.Okko,
                SupplierPricePerLiter = 40m,
                MarginUahPerLiter = 3m,
                PumpPricePerLiter = pump,
                MinDiscountPerLiter = 1m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            // The sizes it is sold in, in the real pool. The pool lives on the size, so this is
            // where "which pool is this fuel in" is answered.
            foreach (var liters in new[] { 10m, 50m })
            {
                _context.FuelNominals.Add(new FuelNominal
                {
                    FuelTypeId = fuelId,
                    DataKind = DataKinds.Real,
                    Liters = liters,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
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
        var gas = await FuelOfAsync("okko-gas");
        gas.PumpPricePerLiter.Should().Be(47.90m);
        var diesel = await FuelOfAsync("okko-dp");
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
        var fuel = await FuelOfAsync("okko-95");
        fuel.PumpPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task Sync_ShouldKeepTheLastKnownPrices_WhenTheFetchThrowsAnythingElse()
    {
        await SeedAsync(pump: 50m);
        var client = new StubClient(() => throw new HttpRequestException("connection reset"));
        await Service(client, enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();
        (await FuelOfAsync("okko-95"))
            .PumpPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task Sync_ShouldRefuseAPumpThatWouldSellBelowCost_AndSaySo()
    {
        // A-95 costs 40 + 3 margin = 43. A pump of 41 gives a ceiling of 40, which is at cost and
        // below the moment minDiscount pushes under it - and a below-cost fuel is refused at checkout,
        // so accepting this automatically would silently take the fuel off sale.
        await SeedAsync(pump: 50m);
        var cheap = new[] { new OkkoPumpPrice("A-95", 40.50m) };
        var result = await new ImportOkkoPumpPricesCommandHandler(_context, new BrandSlugResolver(_context), _events, new PriceHistoryRecorder(_context))
            .ApplyAsync(cheap, dryRun: false, Guid.Empty, "OKKO price sync");
        result.BelowCostSkipped.Should().ContainSingle().Which.Should().Be("A-95");
        result.FuelsChanged.Should().Be(0);
        _context.ChangeTracker.Clear();
        (await FuelOfAsync("okko-95"))
            .PumpPricePerLiter.Should().Be(50m);
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
        var a95 = await _context.FuelTypes.FindAsync(Id("okko-95"));
        a95!.AllowBelowCost = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var result = await new ImportOkkoPumpPricesCommandHandler(_context, new BrandSlugResolver(_context), _events, new PriceHistoryRecorder(_context))
            .ApplyAsync([new OkkoPumpPrice("A-95", 40.50m)], dryRun: false, Guid.Empty, "OKKO price sync");
        result.BelowCostSkipped.Should().BeEmpty();
        result.FuelsChanged.Should().Be(1);
        _context.ChangeTracker.Clear();
        (await FuelOfAsync("okko-95"))
            .PumpPricePerLiter.Should().Be(40.50m);
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
        var result = await new ImportOkkoPumpPricesCommandHandler(_context, new BrandSlugResolver(_context), _events, new PriceHistoryRecorder(_context))
            .ApplyAsync(mixed, dryRun: false, Guid.Empty, "OKKO price sync");
        result.BelowCostSkipped.Should().ContainSingle();
        result.FuelsChanged.Should().Be(1);
        _context.ChangeTracker.Clear();
        (await FuelOfAsync("okko-gas"))
            .PumpPricePerLiter.Should().Be(47.90m);
    }

    [Fact]
    public async Task Sync_ShouldAddAGradeTheCatalogueLacks_BeforeItPricesAnything()
    {
        // The ordering is the contract: a fuel that does not exist yet has nowhere to hold a pump
        // price, so the catalogue half runs first and the pricing half then sees the new fuel. One pass
        // has to leave the catalogue complete AND priced, or a grade sits unpriced until the next run.
        _context.AddMissing(BrandIds.Okko);
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = Id("okko-95"), Name = "A-95", BrandId = BrandIds.Okko, BrandFuelCode = "A-95",
            BasePrice = 54, DiscountPrice = 51
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var sheet = new[]
        {
            new OkkoPumpPrice("A-95", 92.90m, "А-95"),   // known already
            new OkkoPumpPrice("DP", 99.90m, "ДП ЄВРО")  // new: the catalogue has no diesel
        };
        // The weekly job: the catalogue half runs first, because a fuel that does not exist yet
        // has nowhere to hold a pump price. The fifteen-minute job only moves prices - creating a
        // grade is a decision, and it belongs to the weekly one.
        await Service(new StubClient(() => sheet), enabled: true).SyncCatalogueAndPricesAsync();
        _context.ChangeTracker.Clear();
        var fuels = await _context.FuelTypes.ToListAsync();
        fuels.Select(f => $"{f.Name}/{f.BrandFuelCode}").Should().Contain("ДП/DP");
        var created = fuels.Single(f => f.BrandFuelCode == "DP");        created.Name.Should().Be(
            "ДП",
            "the name comes from the grade the code identifies; the site's own label is a rendering of it");
        // The new fuel carries the published pump and nothing else, and stays off sale: no cost,
        // no margin. It waits for an operator rather than going live at zero.
        created.PumpPricePerLiter.Should().Be(99.90m);
        created.SupplierPricePerLiter.Should().BeNull();
        // And the fuel that already existed was priced, not recreated.
        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Okko)).Should().Be(2);
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
        var fuel = await FuelOfAsync("okko-95");
        fuel.SupplierPricePerLiter.Should().Be(40m);
        // The margin is the fuel's too - the same place production reads it since the offer
        // stopped carrying a copy.
        fuel.MarginUahPerLiter.Should().Be(3m);
    }

    [Fact]
    public async Task Sync_ShouldAuditEveryPriceItChanges_Unattended()
    {
        await SeedAsync(pump: 50m);
        await Service(new StubClient(() => Sheet), enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();
        // Nobody typed these numbers, so the audit trail is the only way to answer "who moved this
        // price and when" - it must name the scheduler rather than leave the actor blank.
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
        // runs in the same job - after pass one there is nothing left uncoded, so it must be silent too.
        var afterSecond = await _context.AuditEventOutbox.AsNoTracking()
            .GroupBy(e => e.EventType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();
        afterSecond.Should().BeEquivalentTo(afterFirst,
            "a second pass with the same sheet changes nothing and must say nothing");
    }

    /// <summary>
    /// The fuel a fixture name stands for. Every figure these tests assert on - pump, cost, margin -
    /// is the fuel's, and the size it is sold in no longer identifies a price of its own.
    /// </summary>
    private Task<FuelTypeEntity> FuelOfAsync(string key)
        => _context.FuelTypes.AsNoTracking().FirstAsync(f => f.Id == Id(key));

    private Guid Id(string key)
    {
        if (!_ids.TryGetValue(key, out var id))
        {
            id = Guid.NewGuid();
            _ids[key] = id;
        }
        return id;
    }
}
