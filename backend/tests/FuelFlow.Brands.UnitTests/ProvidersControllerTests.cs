using FuelFlow.Features.Brands.GetFuelPriceHistory;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.Providers.UnitTests;

public sealed class BrandsControllerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly BrandsController _controller;
    private readonly ClaimsPrincipal _user;

    public BrandsControllerTests()
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
    private BrandsController BuildController(NotificationDispatcher notifications) =>
        new(
            new GetBrandsQueryHandler(_context),
            new GetBrandByIdQueryHandler(_context),
            new GetBrandHistoryQueryHandler(_context),
            new GetFuelPriceHistoryQueryHandler(_context),
            new GetFuelPriceAtQueryHandler(_context),
            new AuditEventService(_context),
            _context,
            notifications,
            new OfferPriceRecorder(_context))
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
            MinDiscountPerLiter = 0.5m,
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
            MinDiscountPerLiter = 0.5m,
            AllowBelowCost = allowBelowCost,
            // Stated, not defaulted - see BelowCostCreate.
            IsTestData = false
        };

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task AddStationAsync(string id, string name)
    {
        _context.Stations.Add(new Station
        {
            Id = id,
            Name = name,
            LogoText = name,
            Color = "#ffffff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private async Task AddFuelAsync(string id, string stationId, string name)
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = id,
            StationId = stationId,
            Name = name,
            BasePrice = 100,
            DiscountPrice = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
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
    private async Task AddPackageAsync(string fuelId, string stationId, decimal liters, decimal final, decimal supplier, decimal margin)
    {
        _context.SeedPackage(
            id: $"{fuelId}-{liters}",
            stationId: stationId,
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
        await AddStationAsync("test-klo", "KLO");

        var result = await _controller.AddFuel(
            "test-klo",
            FuelRequest("ttt", 100m, 0.5m, 100.5m),
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.StationId == "test-klo");
        var liters = await _context.FuelOfferVolumes
            .Where(p => p.Offer.FuelTypeId == fuel.Id)
            .Select(p => (int)p.Liters)
            .OrderBy(l => l)
            .ToListAsync();

        liters.Should().Equal(2, 3, 5, 10, 20, 50);

        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == fuel.Id);
        offer.ComputedFinalPricePerLiter.Should().Be(100.5m);
    }

    [Fact]
    public async Task AddFuel_ProviderWithExistingNominals_UsesThoseNominals()
    {
        await AddStationAsync("test-okko", "OKKO");
        await AddFuelAsync("test-fuel-a", "test-okko", "A-95");
        await AddPackageAsync("test-fuel-a", "test-okko", 10m, 51m, 49m, 2m);
        await AddPackageAsync("test-fuel-a", "test-okko", 20m, 51m, 49m, 2m);

        var result = await _controller.AddFuel(
            "test-okko",
            FuelRequest("B", 50m, 2m, 52m),
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        var newFuel = await _context.FuelTypes.SingleAsync(f => f.StationId == "test-okko" && f.Id != "test-fuel-a");
        var liters = await _context.FuelOfferVolumes
            .Where(p => p.Offer.FuelTypeId == newFuel.Id)
            .Select(p => (int)p.Liters)
            .OrderBy(l => l)
            .ToListAsync();

        liters.Should().Equal(10, 20);
    }

    [Fact]
    public async Task AddFuel_UnknownProvider_ReturnsNotFound()
    {
        var result = await _controller.AddFuel(
            "missing",
            FuelRequest("X", 50m, 1m, 51m),
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // Regression for #113: the per-liter headline (base/discount) and the package total must carry
    // kopecks, not round to whole UAH. supplier 95 + margin 2.86 ⇒ final 97.86 /L, 10 L ⇒ 978.60.
    [Fact]
    public async Task AddFuel_FractionalPerLiterPrice_PreservesKopecksOnHeadlineAndPackage()
    {
        await AddStationAsync("test-okko", "OKKO");

        var result = await _controller.AddFuel(
            "test-okko",
            FuelRequest("ДП ЄВРО", 95m, 2.86m, 97.86m, 10),
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.StationId == "test-okko");
        fuel.BasePrice.Should().Be(97.86m);
        fuel.DiscountPrice.Should().Be(97.86m);

        var volume = await _context.FuelOfferVolumes.SingleAsync(v => v.Offer.FuelTypeId == fuel.Id);
        // No override on a fuel added through the brand screen: the package price is the rate x litres.`n        volume.PriceOverrideUah.Should().BeNull();`n        volume.Liters.Should().Be(10m);
    }

    [Fact]
    public async Task UpdateFuel_FuelWithoutPackages_SeedsDefaultPackagesAndSavesPrice()
    {
        await AddStationAsync("test-klo", "KLO");
        await AddFuelAsync("test-fuel-ttt", "test-klo", "ttt");

        var result = await _controller.UpdateFuel(
            "test-fuel-ttt",
            FuelDto("test-fuel-ttt", "ttt", 100m, 0.5m, 100.5m),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var liters = await _context.FuelOfferVolumes
            .Where(p => p.Offer.FuelTypeId == "test-fuel-ttt")
            .Select(p => (int)p.Liters)
            .OrderBy(l => l)
            .ToListAsync();
        liters.Should().Equal(2, 3, 5, 10, 20, 50);

        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == "test-fuel-ttt");
        offer.ComputedFinalPricePerLiter.Should().Be(100.5m);
    }

    [Fact]
    public async Task UpdateFuel_AppendsTheNewPrice_ToTheOfferHistory_WithTheOperatorAsActor()
    {
        await AddStationAsync("test-okko", "OKKO");
        await AddFuelAsync("test-fuel-h", "test-okko", "A-95");
        await AddPackageAsync("test-fuel-h", "test-okko", 10m, 51m, 49m, 2m);

        var actingUser = Guid.Parse(
            _controller.ControllerContext.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        await _controller.UpdateFuel(
            "test-fuel-h",
            FuelDto("test-fuel-h", "A-95", 60m, 3m, 63m),
            CancellationToken.None);
        _context.ChangeTracker.Clear();

        await _controller.UpdateFuel(
            "test-fuel-h",
            FuelDto("test-fuel-h", "A-95", 60m, 4m, 64m),
            CancellationToken.None);
        _context.ChangeTracker.Clear();

        var history = await _context.FuelOfferPrices
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
        history.Should().OnlyContain(p => p.Reason == OfferPriceReasons.Operator);
        history.Should().OnlyContain(p => p.ChangedByUserId == actingUser);
    }

    [Fact]
    public async Task UpdateFuel_WithExistingPackages_UpdatesAllPrices()
    {
        await AddStationAsync("test-okko", "OKKO");
        await AddFuelAsync("test-fuel-a", "test-okko", "A-95");
        await AddPackageAsync("test-fuel-a", "test-okko", 10m, 51m, 49m, 2m);
        await AddPackageAsync("test-fuel-a", "test-okko", 20m, 51m, 49m, 2m);

        var result = await _controller.UpdateFuel(
            "test-fuel-a",
            FuelDto("test-fuel-a", "A-95", 60m, 3m, 63m),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == "test-fuel-a");
        offer.ComputedFinalPricePerLiter.Should().Be(63m);
        offer.SupplierPricePerLiter.Should().Be(60m);

        // The frozen package total is restated on every volume from the new per-litre price. Asserting
        // this separately from the offer is the point: it is what stops a stale package total from
        // surviving a reprice, which is the failure the old per-package columns allowed.
        var volumes = await _context.FuelOfferVolumes
            .Where(v => v.OfferId == offer.Id).ToListAsync();
        // Nothing to restate per volume any more: the offer's rate moved, and every nominal follows it.`n        // What used to be checked here - that each volume's stored total was rewritten - is now`n        // structural: there is no stored total. Asserted instead that no override was invented.`n        volumes.Should().OnlyContain(v => v.PriceOverrideUah == null);
    }

    [Fact]
    public async Task UpdateFuel_RecordsOldToNewValuesInAuditSummary()
    {
        await AddStationAsync("test-okko", "OKKO");
        await AddFuelAsync("test-fuel-a", "test-okko", "A-95");
        await AddPackageAsync("test-fuel-a", "test-okko", 10m, 51m, 49m, 2m);

        await _controller.UpdateFuel(
            "test-fuel-a",
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
            "missing",
            FuelDto("missing", "X", 50m, 1m, 51m),
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // --- UpdateNominals ---------------------------------------------------

    [Fact]
    public async Task UpdateNominals_ReplacesPackagesForAllFuels()
    {
        await AddStationAsync("test-okko", "OKKO");
        await AddFuelAsync("test-fuel-a", "test-okko", "A-95");
        await AddFuelAsync("test-fuel-b", "test-okko", "B");
        await AddPackageAsync("test-fuel-a", "test-okko", 10m, 51m, 49m, 2m);
        await AddPackageAsync("test-fuel-b", "test-okko", 20m, 52m, 50m, 2m);

        var result = await _controller.UpdateNominals(
            "test-okko",
            new List<int> { 5, 10 },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        foreach (var fuelId in new[] { "test-fuel-a", "test-fuel-b" })
        {
            var liters = await _context.FuelOfferVolumes
                .Where(p => p.Offer.FuelTypeId == fuelId)
                .Select(p => (int)p.Liters)
                .OrderBy(l => l)
                .ToListAsync();
            liters.Should().Equal(5, 10);
        }
    }

    [Fact]
    public async Task UpdateNominals_PreservesPerLiterPricingFromOldPackages()
    {
        await AddStationAsync("test-okko", "OKKO");
        await AddFuelAsync("test-fuel-a", "test-okko", "A-95");
        await AddPackageAsync("test-fuel-a", "test-okko", 10m, 51m, 49m, 2m);

        var result = await _controller.UpdateNominals(
            "test-okko",
            new List<int> { 20 },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == "test-fuel-a");
        offer.ComputedFinalPricePerLiter.Should().Be(51m);
        offer.SupplierPricePerLiter.Should().Be(49m);
        offer.MarginUahPerLiter.Should().Be(2m);

        var volume = await _context.FuelOfferVolumes.SingleAsync(v => v.OfferId == offer.Id);
        volume.Liters.Should().Be(20m);
        volume.PriceOverrideUah.Should().BeNull("a nominal priced by the operator stays null and follows the offer");
    }

    [Fact]
    public async Task UpdateNominals_UnknownProvider_ReturnsNotFound()
    {
        var result = await _controller.UpdateNominals(
            "missing",
            new List<int> { 5 },
            CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // --- Below-cost hard block + loss-leader alert (slice 3, operator write path) ----------

    [Fact]
    public async Task AddFuel_BelowCostNotOptedIn_Returns400_AndPersistsNothing()
    {
        await AddStationAsync("test-okko", "OKKO");

        var result = await _controller.AddFuel(
            "test-okko",
            BelowCostCreate("ДП збиток-add-block", allowBelowCost: false),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(bad.Value).Should().Contain("below_cost");

        (await _context.FuelTypes.AnyAsync(f => f.StationId == "test-okko")).Should().BeFalse();
        // Deleting a brand archives its offers AND their volumes now, so "no live package at this
        // station" has to be asked of the volumes behind them - the offer alone would still answer.
        (await _context.FuelOfferVolumes.AnyAsync(v => v.Offer.StationId == "test-okko")).Should().BeFalse();
        (await _context.FuelOffers.IgnoreQueryFilters().AnyAsync(o => o.StationId == "test-okko" && !o.IsDeleted))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AddFuel_BelowCostOptedIn_SavesPrice_AndFiresDeliberateWarningAlert()
    {
        await AddStationAsync("test-okko", "OKKO");
        var alerts = new Mock<IAlertNotifier>();
        var controller = BuildController(DispatcherWith(alerts));

        var result = await controller.AddFuel(
            "test-okko",
            BelowCostCreate("ДП збиток-add-ok", allowBelowCost: true),
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.StationId == "test-okko");
        fuel.AllowBelowCost.Should().BeTrue();
        var pricedOffer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == fuel.Id);
        pricedOffer.ComputedFinalPricePerLiter.Should().Be(48.5m);   // min(cost 50 + margin 2, pump 49 − minDisc 0.5)

        alerts.Verify(a => a.SendAsync(
            AlertSeverity.Warning, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateFuel_BelowCostNotOptedIn_Returns400_AndLeavesPriceUnchanged()
    {
        await AddStationAsync("test-okko", "OKKO");
        await AddFuelAsync("test-fuel-a", "test-okko", "A-95");
        await AddPackageAsync("test-fuel-a", "test-okko", 10m, 51m, 49m, 2m);

        var result = await _controller.UpdateFuel(
            "test-fuel-a",
            BelowCostUpdate("test-fuel-a", "ДП збиток-upd-block", allowBelowCost: false),
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        JsonSerializer.Serialize(bad.Value).Should().Contain("below_cost");

        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == "test-fuel-a");
        offer.ComputedFinalPricePerLiter.Should().Be(51m);
        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == "test-fuel-a");
        fuel.AllowBelowCost.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateFuel_BelowCostOptedIn_SavesPrice_AndFiresDeliberateWarningAlert()
    {
        await AddStationAsync("test-okko", "OKKO");
        await AddFuelAsync("test-fuel-a", "test-okko", "A-95");
        await AddPackageAsync("test-fuel-a", "test-okko", 10m, 51m, 49m, 2m);
        var alerts = new Mock<IAlertNotifier>();
        var controller = BuildController(DispatcherWith(alerts));

        var result = await controller.UpdateFuel(
            "test-fuel-a",
            BelowCostUpdate("test-fuel-a", "ДП збиток-upd-ok", allowBelowCost: true),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == "test-fuel-a");
        offer.ComputedFinalPricePerLiter.Should().Be(48.5m);
        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == "test-fuel-a");
        fuel.AllowBelowCost.Should().BeTrue();

        alerts.Verify(a => a.SendAsync(
            AlertSeverity.Warning, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
