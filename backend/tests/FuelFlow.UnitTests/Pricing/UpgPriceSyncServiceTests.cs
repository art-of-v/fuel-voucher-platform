using FuelFlow.BackgroundJobs;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.ImportUpgWogPrices;
using FuelFlow.Features.Pricing.ScrapeUpgWogPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// The scheduled UPG refresh. What matters here is not the arithmetic - the importer has its own
/// tests - but the unattended failure modes: a fetch that fails must not move a price, and a
/// disabled switch must not reach the network at all.
/// </summary>
/// <remarks>
/// WOG is not one of these: its page answers production with an empty 204 since 2026-10-29, and it
/// is priced from Minfin's table instead. The test below pins that this job does not ask for it.
/// </remarks>
public sealed class UpgPriceSyncServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public UpgPriceSyncServiceTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>A client stub that either answers or blows up, and records what it was asked for.</summary>
    private sealed class StubClient : IUpgWogPriceClient
    {
        private readonly Func<string, IReadOnlyList<UpgWogPrice>> _respond;
        public List<string> Asked { get; } = new();

        public StubClient(Func<string, IReadOnlyList<UpgWogPrice>> respond) => _respond = respond;

        public Task<IReadOnlyList<UpgWogPrice>> FetchAsync(string brand, CancellationToken ct = default)
        {
            Asked.Add(brand);
            return Task.FromResult(_respond(brand));
        }
    }

    private UpgPriceSyncService Service(StubClient client, bool enabled, ILogger<UpgPriceSyncService>? logger = null)
        => new(
            client,
            new ImportUpgWogPricesCommandHandler(_context, new PriceHistoryRecorder(_context), new BrandSlugResolver(_context)),
            _context,
            Options.Create(new UpgWogPriceOptions { Enabled = enabled }),
            logger ?? NullLogger<UpgPriceSyncService>.Instance);

    private async Task SeedBrandAsync(Guid brandId, string slug)
    {
        _context.Brands.Add(new Brand
        {
            Id = brandId,
            Slug = slug,
            Name = slug.ToUpperInvariant(),
            LogoText = slug.ToUpperInvariant(),
            Color = "#000",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Sync_ShouldNotReachTheNetwork_WhenDisabled()
    {
        var client = new StubClient(_ => []);

        await Service(client, enabled: false).SyncPricesAsync();

        // A fresh deployment must be able to sit with the switch off without polling two vendors.
        client.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_ShouldAskOnlyTheVendorWhosePageStillAnswers()
    {
        await SeedBrandAsync(BrandIds.Upg, "upg");
        await SeedBrandAsync(BrandIds.Wog, "wog");
        var client = new StubClient(_ => [new UpgWogPrice("upg95", 91.90m)]);

        await Service(client, enabled: true).SyncPricesAsync();

        // WOG's page answers this host with an empty 204 (2026-10-29), so asking it every quarter
        // hour is a fetch that can only fail - and a second source writing the same fuels would
        // fight Minfin's table, which is where WOG is priced from now.
        client.Asked.Should().BeEquivalentTo(["upg"]);
    }

    [Fact]
    public async Task Sync_ShouldKeepTheLastKnownPrices_WhenTheFetchFails()
    {
        await SeedBrandAsync(BrandIds.Upg, "upg");
        var client = new StubClient(_ => throw new UpgWogPriceScrapeException("HTTP 503"));

        await Service(client, enabled: true).SyncPricesAsync();

        // The important one: an outage must leave yesterday's prices intact, not blank them. This
        // importer never archives a fuel the sheet stopped carrying, so a bad fetch cannot empty the
        // catalogue either - and it wrote nothing at all.
        (await _context.FuelTypes.CountAsync()).Should().Be(0);
        (await _context.FuelPriceHistory.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Sync_ShouldCreateAGradeTheCatalogueLacks_WithItsSizes()
    {
        await SeedBrandAsync(BrandIds.Upg, "upg");
        var client = new StubClient(_ => [new UpgWogPrice("upg95", 91.90m)]);

        // The weekly catalogue check is what brings a new grade in; the fifteen-minute price refresh
        // only moves prices. The split is the whole reason there are two jobs.
        await Service(client, enabled: true).SyncCatalogueAndPricesAsync();
        _context.ChangeTracker.Clear();

        var created = await _context.FuelTypes.SingleAsync(f => f.BrandId == BrandIds.Upg);
        created.BrandFuelCode.Should().Be("upg95");
        created.PumpPricePerLiter.Should().Be(91.90m);
        // No cost and no margin: the importer knows what the vendor charges and nothing about what
        // we pay, so the fuel waits for an operator rather than going live at zero.
        created.SupplierPricePerLiter.Should().BeNull();
        created.MarginUahPerLiter.Should().BeNull();
        // The sizes, though - the importer creates them, which is what puts the grade on the shelf.
        (await _context.FuelNominals.CountAsync(n => n.FuelTypeId == created.Id)).Should().Be(3);
    }

    [Fact]
    public async Task Sync_ShouldRecordThePriceInTheHistory()
    {
        await SeedBrandAsync(BrandIds.Upg, "upg");
        var client = new StubClient(_ => [new UpgWogPrice("upg95", 91.90m)]);

        await Service(client, enabled: true).SyncCatalogueAndPricesAsync();
        _context.ChangeTracker.Clear();

        // Nobody typed this number, so the history is the only way to answer "what was this fuel priced
        // at" - the row exists from the moment the fuel was born, not from the first time somebody
        // edited it.
        var history = await _context.FuelPriceHistory.AsNoTracking().ToListAsync();
        history.Should().ContainSingle();
        history[0].PumpPricePerLiter.Should().Be(91.90m);
        history[0].FuelTypeId.Should().Be((await _context.FuelTypes.SingleAsync(f => f.BrandId == BrandIds.Upg)).Id);
    }
}
