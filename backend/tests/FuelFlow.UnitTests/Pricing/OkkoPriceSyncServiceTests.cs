using FluentAssertions;
using FuelFlow.BackgroundJobs;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
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
    private readonly ProviderEventService _events;

    public OkkoPriceSyncServiceTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        _events = new ProviderEventService(_context);
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

    private OkkoPriceSyncService Service(StubClient client, bool enabled)
        => new(
            client,
            new ImportOkkoPumpPricesCommandHandler(_context, _events),
            Options.Create(new OkkoPriceOptions { Enabled = enabled }),
            NullLogger<OkkoPriceSyncService>.Instance);

    /// <summary>The production seed shape: five OKKO fuels, each with a cost/margin/pump already set.</summary>
    private async Task SeedAsync(decimal pump = 50m)
    {
        _context.Stations.Add(new Station { Id = "okko", Name = "OKKO", Color = "#000", LogoText = "OKKO" });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko" },
            new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko" },
            new FuelTypeEntity { Id = "okko-gas", Name = "ГАЗ", StationId = "okko" },
            new FuelTypeEntity { Id = "okko-p95", Name = "Pulls 95", StationId = "okko" },
            new FuelTypeEntity { Id = "okko-pulls-dp", Name = "ДП PULLS", StationId = "okko" });

        foreach (var (fuelTypeId, fuelName) in new[]
                 {
                     ("okko-95", "A-95"), ("okko-dp", "ДП ЄВРО"), ("okko-gas", "ГАЗ"),
                     ("okko-p95", "Pulls 95"), ("okko-pulls-dp", "ДП PULLS"),
                 })
        {
            foreach (var liters in new[] { 10m, 50m })
            {
                _context.FuelPackages.Add(new FuelPackage
                {
                    Id = $"{fuelTypeId}-{liters}",
                    StationId = "okko",
                    FuelTypeId = fuelTypeId,
                    FuelName = fuelName,
                    Liters = liters,
                    SupplierPricePerLiter = 40m,
                    MarginUahPerLiter = 3m,
                    PumpPricePerLiter = pump,
                    MinDiscountPerLiter = 1m,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
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

        var gas = await _context.FuelPackages.AsNoTracking()
            .FirstAsync(p => p.Id == "okko-gas-50");
        gas.PumpPricePerLiter.Should().Be(47.90m);

        var diesel = await _context.FuelPackages.AsNoTracking()
            .FirstAsync(p => p.Id == "okko-dp-50");
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
        var pkg = await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-95-50");
        pkg.PumpPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task Sync_ShouldKeepTheLastKnownPrices_WhenTheFetchThrowsAnythingElse()
    {
        await SeedAsync(pump: 50m);
        var client = new StubClient(() => throw new HttpRequestException("connection reset"));

        await Service(client, enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        (await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-95-50"))
            .PumpPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task Sync_ShouldRefuseAPumpThatWouldSellBelowCost_AndSaySo()
    {
        // A-95 costs 40 + 3 margin = 43. A pump of 41 gives a ceiling of 40, which is at cost and
        // below the moment minDiscount pushes under it — and a below-cost fuel is refused at checkout,
        // so accepting this automatically would silently take the fuel off sale.
        await SeedAsync(pump: 50m);
        var cheap = new[] { new OkkoPumpPrice("A-95", 40.50m) };

        var result = await new ImportOkkoPumpPricesCommandHandler(_context, _events)
            .ApplyAsync(cheap, dryRun: false, Guid.Empty, "OKKO price sync");

        result.BelowCostSkipped.Should().ContainSingle().Which.Should().Be("A-95");
        result.FuelsChanged.Should().Be(0);
        _context.ChangeTracker.Clear();
        (await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-95-50"))
            .PumpPricePerLiter.Should().Be(50m);
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

        var result = await new ImportOkkoPumpPricesCommandHandler(_context, _events)
            .ApplyAsync([new OkkoPumpPrice("A-95", 40.50m)], dryRun: false, Guid.Empty, "OKKO price sync");

        result.BelowCostSkipped.Should().BeEmpty();
        result.FuelsChanged.Should().Be(1);
        _context.ChangeTracker.Clear();
        (await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-95-50"))
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

        var result = await new ImportOkkoPumpPricesCommandHandler(_context, _events)
            .ApplyAsync(mixed, dryRun: false, Guid.Empty, "OKKO price sync");

        result.BelowCostSkipped.Should().ContainSingle();
        result.FuelsChanged.Should().Be(1);
        _context.ChangeTracker.Clear();
        (await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-gas-50"))
            .PumpPricePerLiter.Should().Be(47.90m);
    }

    [Fact]
    public async Task Sync_ShouldHoldSupplierCost_AndOnlyMoveTheCeiling()
    {
        await SeedAsync(pump: 50m);

        await Service(new StubClient(() => Sheet), enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        // A pump change is not a purchase-cost event.
        var pkg = await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-95-50");
        pkg.SupplierPricePerLiter.Should().Be(40m);
        pkg.MarginUahPerLiter.Should().Be(3m);
    }

    [Fact]
    public async Task Sync_ShouldAuditEveryPriceItChanges_Unattended()
    {
        await SeedAsync(pump: 50m);

        await Service(new StubClient(() => Sheet), enabled: true).SyncPricesAsync();
        _context.ChangeTracker.Clear();

        // Nobody typed these numbers, so the audit trail is the only way to answer "who moved this
        // price and when" — it must name the scheduler rather than leave the actor blank.
        var events = await _context.ProviderEventOutbox.AsNoTracking()
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

        var second = Service(new StubClient(() => Sheet), enabled: true);
        await second.SyncPricesAsync();
        _context.ChangeTracker.Clear();

        // No new audit rows on the second pass: the provider history must not fill with daily
        // no-ops that say "nothing changed" every morning.
        (await _context.ProviderEventOutbox.AsNoTracking().CountAsync()).Should().Be(5);
    }
}