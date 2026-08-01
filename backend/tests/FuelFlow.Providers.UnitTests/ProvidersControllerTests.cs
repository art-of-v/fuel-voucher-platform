using System.Security.Claims;
using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Providers.GetProviderById;
using FuelFlow.Features.Providers.GetProviderHistory;
using FuelFlow.Features.Providers.GetProviders;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Providers.UnitTests;

public sealed class ProvidersControllerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ProvidersController _controller;

    public ProvidersControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _controller = new ProvidersController(
            new GetProvidersQueryHandler(_context),
            new GetProviderByIdQueryHandler(_context),
            new GetProviderHistoryQueryHandler(_context),
            new ProviderEventService(_context),
            _context);

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "Test Admin"),
            new Claim("first_name", "Test"),
            new Claim("last_name", "Admin")
        ], "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

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

    // --- UpdateFuel -------------------------------------------------------

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
        packages.Should().OnlyContain(p => p.Price == (int)Math.Round(63m * p.Liters));
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
        pkg.Price.Should().Be((int)Math.Round(51m * 20m));
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
}
