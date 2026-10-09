using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Providers.GetProviderById;
using FuelFlow.Features.Providers.GetProviderHistory;
using FuelFlow.Features.Providers.GetProviders;
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

public sealed class ProvidersControllerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ProvidersController _controller;
    private readonly ClaimsPrincipal _user;

    public ProvidersControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
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
    private ProvidersController BuildController(NotificationDispatcher notifications) =>
        new(
            new GetProvidersQueryHandler(_context),
            new GetProviderByIdQueryHandler(_context),
            new GetProviderHistoryQueryHandler(_context),
            new ProviderEventService(_context),
            _context,
            notifications)
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
            PackageLiters = new List<int> { 10 }
        };

    private static ProviderFuelDto BelowCostUpdate(string id, string name, bool allowBelowCost) =>
        new()
        {
            Id = id,
            Name = name,
            SupplierPricePerLiter = 50m,
            MarginUahPerLiter = 2m,
            PumpPricePerLiter = 49m,
            MinDiscountPerLiter = 0.5m,
            AllowBelowCost = allowBelowCost
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

    private async Task AddPackageAsync(string fuelId, string stationId, decimal liters, decimal final, decimal supplier, decimal margin)
    {
        _context.FuelPackages.Add(new FuelPackage
        {
            Id = $"{fuelId}-{liters}",
            StationId = stationId,
            FuelTypeId = fuelId,
            FuelName = "fuel",
            Liters = liters,
            Price = (int)Math.Round(final * liters),
            OriginalPrice = (int)Math.Round(supplier * liters),
            SupplierPricePerLiter = supplier,
            MarginUahPerLiter = margin,
            FinalPricePerLiter = final,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private static CreateFuelRequest FuelRequest(string name, decimal supplier, decimal margin, decimal final, params int[] packageLiters) =>
        new()
        {
            Name = name,
            SupplierPricePerLiter = supplier,
            MarginUahPerLiter = margin,
            FinalPricePerLiter = final,
            PackageLiters = packageLiters.ToList()
        };

    private static ProviderFuelDto FuelDto(string id, string name, decimal supplier, decimal margin, decimal final) =>
        new()
        {
            Id = id,
            Name = name,
            SupplierPricePerLiter = supplier,
            MarginUahPerLiter = margin,
            FinalPricePerLiter = final
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
        var liters = await _context.FuelPackages
            .Where(p => p.FuelTypeId == fuel.Id)
            .Select(p => (int)p.Liters)
            .OrderBy(l => l)
            .ToListAsync();

        liters.Should().Equal(2, 3, 5, 10, 20, 50);

        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == fuel.Id).ToListAsync();
        packages.Should().OnlyContain(p => p.FinalPricePerLiter == 100.5m);
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
        var liters = await _context.FuelPackages
            .Where(p => p.FuelTypeId == newFuel.Id)
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

        var pkg = await _context.FuelPackages.SingleAsync(p => p.FuelTypeId == fuel.Id);
        pkg.Price.Should().Be(978.60m);
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

        var liters = await _context.FuelPackages
            .Where(p => p.FuelTypeId == "test-fuel-ttt")
            .Select(p => (int)p.Liters)
            .OrderBy(l => l)
            .ToListAsync();
        liters.Should().Equal(2, 3, 5, 10, 20, 50);

        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == "test-fuel-ttt").ToListAsync();
        packages.Should().OnlyContain(p => p.FinalPricePerLiter == 100.5m);
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

        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == "test-fuel-a").ToListAsync();
        packages.Should().OnlyContain(p => p.FinalPricePerLiter == 63m);
        packages.Should().OnlyContain(p => p.Price == Math.Round(63m * p.Liters, 2, MidpointRounding.AwayFromZero));
        packages.Should().OnlyContain(p => p.SupplierPricePerLiter == 60m);
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

        var evt = await _context.ProviderEventOutbox.SingleAsync(e => e.AggregateType == "Fuel");
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
            var liters = await _context.FuelPackages
                .Where(p => p.FuelTypeId == fuelId)
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

        var pkg = await _context.FuelPackages.SingleAsync(p => p.FuelTypeId == "test-fuel-a");
        pkg.Liters.Should().Be(20m);
        pkg.FinalPricePerLiter.Should().Be(51m);
        pkg.SupplierPricePerLiter.Should().Be(49m);
        pkg.MarginUahPerLiter.Should().Be(2m);
        pkg.Price.Should().Be(Math.Round(51m * 20m, 2, MidpointRounding.AwayFromZero));
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
        (await _context.FuelPackages.AnyAsync(p => p.StationId == "test-okko")).Should().BeFalse();
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
        var pkg = await _context.FuelPackages.SingleAsync(p => p.FuelTypeId == fuel.Id);
        pkg.FinalPricePerLiter.Should().Be(48.5m);   // min(cost 50 + margin 2, pump 49 − minDisc 0.5)

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

        var pkg = await _context.FuelPackages.SingleAsync(p => p.FuelTypeId == "test-fuel-a");
        pkg.FinalPricePerLiter.Should().Be(51m);
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

        var pkg = await _context.FuelPackages.SingleAsync(p => p.FuelTypeId == "test-fuel-a");
        pkg.FinalPricePerLiter.Should().Be(48.5m);
        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == "test-fuel-a");
        fuel.AllowBelowCost.Should().BeTrue();

        alerts.Verify(a => a.SendAsync(
            AlertSeverity.Warning, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
