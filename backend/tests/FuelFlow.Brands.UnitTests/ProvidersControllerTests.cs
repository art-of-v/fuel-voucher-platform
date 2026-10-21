using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Features.Pricing;
using FuelFlow.Brands.UnitTests;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.Providers.UnitTests;

public sealed class BrandFuelControllerTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    /// <summary>Fixture networks: an id AND the network code an import names them by.</summary>
    private static readonly Guid TestOkko = new("0f0f0f0f-0000-4000-8000-00000000000a");
    private static readonly Guid TestWog = new("0f0f0f0f-0000-4000-8000-00000000000b");
    private static readonly Guid TestKlo = new("0f0f0f0f-0000-4000-8000-00000000000c");
    private readonly BrandFuelController _controller;
    private readonly ClaimsPrincipal _user;

    public BrandFuelControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.Database.EnsureCreated();

        _user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Test Admin"),
            new Claim("first_name", "Test"),
            new Claim("last_name", "Admin")
        ], "TestAuth"));

        _controller = BuildController(NotificationDispatcher.Disabled);
    }

    // Builds a controller sharing _context + the admin principal; the dispatcher is swappable so the
    // below-cost tests can assert the loss-leader Telegram alert through a Moq IAlertNotifier.
    private BrandFuelController BuildController(NotificationDispatcher notifications) =>
        new(
        new FuelFlow.Features.Brands.GetFuelPriceHistory.GetFuelPriceHistoryQueryHandler(_context),
            new GetFuelPriceAtQueryHandler(_context),
            new AuditEventService(_context),
            _context,
            notifications,
            new PriceHistoryRecorder(_context))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = _user }
            }
        };

    // Real dispatcher wired to a mock notifier, with below-cost alerts enabled so SendAsync is reached.
    private static NotificationDispatcher DispatcherWith(Mock<IAlertNotifier> alerts) =>
        new(
            alerts.Object,
            Options.Create(new TelegramOptions { Notifications = { NotifyOnBelowCost = true } }),
            NullLogger<NotificationDispatcher>.Instance);

    // Below-cost shape: cost 50, margin 2 (cost-plus 52), but pump 49 − minDiscount 0.5 = 48.5 ceiling
    // binds below cost ⇒ final 48.5 < 50. Each test passes a unique fuel name to dodge the dispatcher's
    // process-static 60-min throttle keyed on belowcost|{fuelName}|{deliberate}.
    private static CreateFuelRequest BelowCostCreate(string name, bool allowBelowCost) =>
        new()
        {
            Name = name,
            SupplierPricePerLiter = 50m,
            MarginUahPerLiter = 2m,
            PumpPricePerLiter = 49m,
            AllowBelowCost = allowBelowCost,
            // Stated, not defaulted: the flag is required on both fuel write paths, and
            // these tests are about pricing rather than rehearsal data.
            IsTestData = false,
            PackageLiters = new List<int> { 10 }
        };

    private static BrandFuelDto BelowCostUpdate(string id, string name, bool allowBelowCost) =>
        new()
        {
            Id = id,
            Name = name,
            SupplierPricePerLiter = 50m,
            MarginUahPerLiter = 2m,
            PumpPricePerLiter = 49m,
            AllowBelowCost = allowBelowCost,
            // Stated, not defaulted - see BelowCostCreate.
            IsTestData = false
        };

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task AddBrandAsync(Guid id, string name)
    {
        _context.Brands.Add(new Brand
        {
            Id = id,
            // Unique per network and what an import file names it by; the controller keys the
            // duplicate check on this rather than on the id.
            Slug = name.ToLowerInvariant(),
            Name = name,
            LogoText = name,
            Color = "#ffffff",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private async Task AddFuelAsync(string id, Guid BrandId, string name)
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid(id),
            BrandId = BrandId,
            Name = name,
            BasePrice = 100,
            DiscountPrice = 100,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Seeds an offer plus one volume, which is what the brand controller now writes and reads.
    /// </summary>
    /// <remarks>
    /// The per-litre figures live on the offer; the volume carries the frozen package total. Seeded the
    /// old way, every assertion below would read a table no handler touches and fail as "the fuel was
    /// not priced" rather than as a broken fixture.
    /// </remarks>
    private async Task AddPackageAsync(string fuelId, Guid BrandId, decimal liters, decimal final, decimal supplier, decimal margin)
    {
        _context.SeedPackage(
            id: $"{fuelId}-{liters}",
            BrandId: BrandId,
            fuelTypeId: fuelId,
            liters: liters,
            price: Math.Round(final * liters, 2),
            supplierPricePerLiter: supplier,
            marginUahPerLiter: margin,
            finalPricePerLiter: final);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private static CreateFuelRequest FuelRequest(string name, decimal supplier, decimal margin, decimal final, params int[] packageLiters) =>
        new()
        {
            Name = name,
            SupplierPricePerLiter = supplier,
            MarginUahPerLiter = margin,
            // Still sent by the admin panel, and still on the request shape, but no longer written
            // anywhere: the offer derives the customer price from cost and margin. Kept here so the
            // tests keep describing the request the panel actually sends.
            FinalPricePerLiter = final,
            PackageLiters = packageLiters.ToList(),
            // Stated, not defaulted - see BelowCostCreate.
            IsTestData = false,
        };

    private static BrandFuelDto FuelDto(string id, string name, decimal supplier, decimal margin, decimal final) =>
        new()
        {
            Id = id,
            Name = name,
            SupplierPricePerLiter = supplier,
            MarginUahPerLiter = margin,
            FinalPricePerLiter = final,
            // Stated, not defaulted - see BelowCostCreate.
            IsTestData = false
        };

    // --- AddFuel ----------------------------------------------------------

    [Fact]
    public async Task AddFuel_ProviderWithoutAnyNominals_CreatesDefaultPackages()
    {
        await AddBrandAsync(TestKlo, "klo");

        var result = await _controller.AddFuel(
            TestKlo,
            FuelRequest("ttt", 100m, 0.5m, 100.5m),
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.BrandId == TestKlo);
        var liters = await _context.FuelNominals
            .Where(n => n.FuelTypeId == fuel.Id)
            .Select(n => (int)n.Liters)
            .OrderBy(l => l)
            .ToListAsync();

        liters.Should().Equal(2, 3, 5, 10, 20, 50);

        fuel.FinalPerLiter().Should().Be(100.5m);
    }

    [Fact]
    public async Task AddFuel_ProviderWithExistingNominals_UsesThoseNominals()
    {
        await AddBrandAsync(TestOkko, "okko");
        await AddFuelAsync("test-fuel-a", TestOkko, "A-95");
        await AddPackageAsync("test-fuel-a", TestOkko, 10m, 51m, 49m, 2m);
        await AddPackageAsync("test-fuel-a", TestOkko, 20m, 51m, 49m, 2m);

        var result = await _controller.AddFuel(
            TestOkko,
            FuelRequest("B", 50m, 2m, 52m),
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();

        var newFuel = await _context.FuelTypes.SingleAsync(f => f.BrandId == TestOkko && f.Id != ToUuid("test-fuel-a"));
        var liters = await _context.FuelNominals
            .Where(n => n.FuelTypeId == newFuel.Id)
            .Select(n => (int)n.Liters)
            .OrderBy(l => l)
            .ToListAsync();

        liters.Should().Equal(10, 20);
    }

    [Fact]
    public async Task AddFuel_UnknownProvider_ReturnsNotFound()
    {
        var result = await _controller.AddFuel(
            Guid.NewGuid(),
            FuelRequest("X", 50m, 1m, 51m),
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // Regression for #113: the per-liter price and the package total must carry kopecks, not round to
    // whole UAH. supplier 95 + margin 2.86 ⇒ 97.86 /L, 10 L ⇒ 978.60.
    [Fact]
    public async Task AddFuel_FractionalPerLiterPrice_PreservesKopecksOnHeadlineAndPackage()
    {
        await AddBrandAsync(TestOkko, "okko");

        var result = await _controller.AddFuel(
            TestOkko,
            FuelRequest("ДП ЄВРО", 95m, 2.86m, 97.86m, 10),
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.BrandId == TestOkko);
        // The price the customer pays is computed from the knobs, so the kopecks are in the
        // computation and not in a stored copy of it.
        fuel.FinalPerLiter().Should().Be(97.86m);
        fuel.BasePrice.Should().BeNull("the reference is the column price, and it is not stored twice");
        fuel.DiscountPrice.Should().BeNull("the price is computed, and it is not stored");

        var volume = await _context.FuelNominals.SingleAsync(n => n.FuelTypeId == fuel.Id);
    }

    [Fact]
    public async Task UpdateFuel_FuelWithoutPackages_SeedsDefaultPackagesAndSavesPrice()
    {
        await AddBrandAsync(TestKlo, "klo");
        await AddFuelAsync("test-fuel-ttt", TestKlo, "ttt");

        var result = await _controller.UpdateFuel(
            ToUuid("test-fuel-ttt"),
            FuelDto("test-fuel-ttt", "ttt", 100m, 0.5m, 100.5m),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var liters = await _context.FuelNominals
            .Where(n => n.FuelTypeId == ToUuid("test-fuel-ttt"))
            .Select(n => (int)n.Liters)
            .OrderBy(l => l)
            .ToListAsync();
        liters.Should().Equal(2, 3, 5, 10, 20, 50);

        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("test-fuel-ttt"));
        fuel.FinalPerLiter().Should().Be(100.5m);
    }

    [Fact]
    public async Task UpdateFuel_AppendsTheNewPrice_ToThePriceHistory_WithTheOperatorAsActor()
    {
        await AddBrandAsync(TestOkko, "okko");
        await AddFuelAsync("test-fuel-h", TestOkko, "A-95");
        await AddPackageAsync("test-fuel-h", TestOkko, 10m, 51m, 49m, 2m);

        var actingUser = Guid.Parse(
            _controller.ControllerContext.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        await _controller.UpdateFuel(
            ToUuid("test-fuel-h"),
            FuelDto("test-fuel-h", "A-95", 60m, 3m, 63m),
            CancellationToken.None);
        _context.ChangeTracker.Clear();

        await _controller.UpdateFuel(
            ToUuid("test-fuel-h"),
            FuelDto("test-fuel-h", "A-95", 60m, 4m, 64m),
            CancellationToken.None);
        _context.ChangeTracker.Clear();

        var history = await _context.FuelPriceHistory
            .OrderBy(p => p.EffectiveFromUtc).ThenBy(p => p.Id)
            .ToListAsync();

        // Two edits, two rows. An edit that only overwrote would leave one row holding the last number
        // and answer "what did it cost before?" with the current price - which is the question the
        // dashboard and reconciliation cannot ask about historical orders any other way.
        history.Should().HaveCount(2);
        history[0].MarginUahPerLiter.Should().Be(3m);
        history[0].FinalPricePerLiter.Should().Be(63m);
        history[1].MarginUahPerLiter.Should().Be(4m);
        history[1].FinalPricePerLiter.Should().Be(64m);
        history.Should().OnlyContain(p => p.Reason == PriceHistoryReasons.Operator);
        history.Should().OnlyContain(p => p.ChangedByUserId == actingUser);
    }

    [Fact]
    public async Task UpdateFuel_WithExistingPackages_UpdatesAllPrices()
    {
        await AddBrandAsync(TestOkko, "okko");
        await AddFuelAsync("test-fuel-a", TestOkko, "A-95");
        await AddPackageAsync("test-fuel-a", TestOkko, 10m, 51m, 49m, 2m);
        await AddPackageAsync("test-fuel-a", TestOkko, 20m, 51m, 49m, 2m);

        var result = await _controller.UpdateFuel(
            ToUuid("test-fuel-a"),
            FuelDto("test-fuel-a", "A-95", 60m, 3m, 63m),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("test-fuel-a"));
        fuel.FinalPerLiter().Should().Be(63m);
        fuel.SupplierPricePerLiter.Should().Be(60m);

        // The frozen package total is restated on every volume from the new per-litre price. Asserting
        // this separately from the offer is the point: it is what stops a stale package total from
        // surviving a reprice, which is the failure the old per-package columns allowed.
        var volumes = await _context.FuelNominals
            .Where(n => n.FuelTypeId == fuel.Id).ToListAsync();
        // Nothing to restate per volume any more: the offer's rate moved, and every nominal follows it.
        // What used to be checked here - that each volume's stored total was rewritten - is now
        // structural: there is no stored total, so there is nothing that could have gone stale.
        volumes.Should().OnlyContain(v => v.Liters > 0m);
    }

    [Fact]
    public async Task UpdateFuel_RecordsOldToNewValuesInAuditSummary()
    {
        await AddBrandAsync(TestOkko, "okko");
        await AddFuelAsync("test-fuel-a", TestOkko, "A-95");
        await AddPackageAsync("test-fuel-a", TestOkko, 10m, 51m, 49m, 2m);

        await _controller.UpdateFuel(
            ToUuid("test-fuel-a"),
            FuelDto("test-fuel-a", "A-95", 100m, 0.5m, 100.5m),
            CancellationToken.None);

        string Num(decimal d) => d.ToString("F2", System.Globalization.CultureInfo.CurrentCulture);

        var evt = await _context.AuditEventOutbox.SingleAsync(e => e.AggregateType == "Fuel");
        evt.EventType.Should().Be("PriceChanged");
        evt.Summary.Should().Contain($"supplier {Num(49m)} → {Num(100m)}");
        evt.Summary.Should().Contain($"margin {Num(2m)} → {Num(0.5m)}");
        evt.Summary.Should().Contain($"final {Num(51m)} → {Num(100.5m)}");
        evt.OldValue.Should().Contain("49");
        evt.NewValue.Should().Contain("100");
    }

    [Fact]
    public async Task UpdateFuel_UnknownFuel_ReturnsNotFound()
    {
        var result = await _controller.UpdateFuel(
            ToUuid("missing"),
            FuelDto("missing", "X", 50m, 1m, 51m),
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // --- UpdateNominals ---------------------------------------------------

    [Fact]
    public async Task UpdateNominals_ReplacesPackagesForAllFuels()
    {
        await AddBrandAsync(TestOkko, "okko");
        await AddFuelAsync("test-fuel-a", TestOkko, "A-95");
        await AddFuelAsync("test-fuel-b", TestOkko, "B");
        await AddPackageAsync("test-fuel-a", TestOkko, 10m, 51m, 49m, 2m);
        await AddPackageAsync("test-fuel-b", TestOkko, 20m, 52m, 50m, 2m);

        var result = await _controller.UpdateNominals(
            TestOkko,
            new List<int> { 5, 10 },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        // The sizes are the fuel's, so both fuels of the brand end up with the same list and the
        // volume table is not the answer to "what does this brand sell in" any more.
        foreach (var fuelId in new[] { "test-fuel-a", "test-fuel-b" })
        {
            var liters = await _context.FuelNominals
                .Where(n => n.FuelTypeId == ToUuid(fuelId))
                .Select(n => (int)n.Liters)
                .OrderBy(l => l)
                .ToListAsync();
            liters.Should().Equal(5, 10);
        }
    }

    [Fact]
    public async Task UpdateNominals_LeavesTheOffersPricingAlone()
    {
        await AddBrandAsync(TestOkko, "okko");
        await AddFuelAsync("test-fuel-a", TestOkko, "A-95");
        await AddPackageAsync("test-fuel-a", TestOkko, 10m, 51m, 49m, 2m);

        var result = await _controller.UpdateNominals(
            TestOkko,
            new List<int> { 20 },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("test-fuel-a"));
        fuel.FinalPerLiter().Should().Be(51m);
        fuel.SupplierPricePerLiter.Should().Be(49m);
        // The margin is the fuel's; the offer's own copy is never written any more.
        fuel.MarginUahPerLiter.Should().Be(2m);

        // The new size exists, and the old one is gone from the fuel's list. What this endpoint no
        // longer does is touch the volume table: it wrote a second copy of the same answer there,
        // once per pool, and keeping the two in step was the cost of having it.
        var liters = await _context.FuelNominals
            .Where(n => n.FuelTypeId == ToUuid("test-fuel-a"))
            .Select(n => (int)n.Liters)
            .ToListAsync();

    }
    [Fact]
    public async Task UpdateNominals_UnknownProvider_ReturnsNotFound()
    {
        var result = await _controller.UpdateNominals(
            Guid.NewGuid(),
            new List<int> { 5 },
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    /// <summary>
    /// The margin a fuel sells at, read from the fuel - the one place it lives.
    /// </summary>
    private static decimal? MarginFor(FuelTypeEntity fuel) => fuel.MarginUahPerLiter;

}
