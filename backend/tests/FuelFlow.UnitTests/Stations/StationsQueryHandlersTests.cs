using FluentAssertions;
using FuelFlow.Features.Stations.GetAdminFuelTypeById;
using FuelFlow.Features.Stations.GetAdminFuelTypes;
using FuelFlow.Features.Stations.GetAdminPackages;
using FuelFlow.Features.Stations.GetAdminPackagesByStation;
using FuelFlow.Features.Stations.GetAdminStationById;
using FuelFlow.Features.Stations.GetAdminStations;
using FuelFlow.Features.Stations.GetPackageSuggestions;
using FuelFlow.Features.Stations.GetPublicPackages;
using FuelFlow.Features.Stations.GetPublicPackagesByStation;
using FuelFlow.Features.Stations.GetPublicStationNodes;
using FuelFlow.Features.Stations.GetPublicStationNodesByStation;
using FuelFlow.Features.Stations.GetPublicStations;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Stations;

public sealed class StationsQueryHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public StationsQueryHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetPublicStations_ShouldReturnSeededStations()
    {
        _context.Stations.AddRange(
            CreateStation("station-b", "Beta Station"),
            CreateStation("station-a", "Alpha Station"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicStationsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicStationsQuery());

        result.Should().HaveCount(2);
        result.Select(s => s.Id).Should().Contain(new[] { "station-a", "station-b" });
        result[0].Name.Should().Be("Alpha Station");
        result[1].Name.Should().Be("Beta Station");
    }

    [Fact]
    public async Task GetPublicStations_ShouldReturnEmptyList_WhenNoStations()
    {
        var handler = new GetPublicStationsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicStationsQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdminStations_ShouldPageResults()
    {
        for (var i = 1; i <= 5; i++)
        {
            _context.Stations.Add(CreateStation($"station-{i}", $"Station {i}"));
        }
        await _context.SaveChangesAsync();

        var handler = new GetAdminStationsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminStationsQuery(Page: 1, PageSize: 2));

        result.Items.Should().HaveCount(2);
        result.Items.Select(s => s.Id).Should().Equal("station-1", "station-2");
        result.TotalCount.Should().Be(5);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.TotalPages.Should().Be(3);
        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdminStations_ShouldReturnLaterPages()
    {
        for (var i = 1; i <= 5; i++)
        {
            _context.Stations.Add(CreateStation($"station-{i}", $"Station {i}"));
        }
        await _context.SaveChangesAsync();

        var handler = new GetAdminStationsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminStationsQuery(Page: 3, PageSize: 2));

        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be("station-5");
        result.HasPreviousPage.Should().BeTrue();
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicStationNodes_ShouldReturnAllNodes()
    {
        _context.StationNodes.AddRange(
            CreateStationNode("node-1", "station-a", "Node A"),
            CreateStationNode("node-2", "station-b", "Node B"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicStationNodesQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicStationNodesQuery());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPublicStationNodesByStation_ShouldFilterByStationId()
    {
        _context.StationNodes.AddRange(
            CreateStationNode("node-1", "station-a", "Node A1"),
            CreateStationNode("node-2", "station-a", "Node A2"),
            CreateStationNode("node-3", "station-b", "Node B"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicStationNodesByStationQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicStationNodesByStationQuery("station-a"));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(n => n.StationId == "station-a");
    }

    [Fact]
    public async Task GetPublicPackages_ShouldReturnAllPackages()
    {
        _context.FuelPackages.AddRange(
            CreatePackage("pkg-1", "station-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-2", "station-b", "ft-b", "Diesel", 20m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicPackagesQuery());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPublicPackagesByStation_ShouldFilterByStationId()
    {
        _context.FuelPackages.AddRange(
            CreatePackage("pkg-1", "station-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-2", "station-a", "ft-a", "A-95", 20m),
            CreatePackage("pkg-3", "station-b", "ft-b", "Diesel", 20m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesByStationQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicPackagesByStationQuery("station-a"));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.StationId == "station-a");
    }

    [Fact]
    public async Task GetAdminFuelTypes_ShouldReturnAllFuelTypes()
    {
        _context.FuelTypes.AddRange(
            CreateFuelType("ft-a", "A-95", "station-a"),
            CreateFuelType("ft-b", "Diesel", "station-b"));
        await _context.SaveChangesAsync();

        var handler = new GetAdminFuelTypesQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminFuelTypesQuery());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAdminFuelTypeById_ShouldReturnFound()
    {
        _context.FuelTypes.Add(CreateFuelType("ft-a", "A-95", "station-a"));
        await _context.SaveChangesAsync();

        var handler = new GetAdminFuelTypeByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminFuelTypeByIdQuery("ft-a"));

        result.Should().NotBeNull();
        result!.Name.Should().Be("A-95");
    }

    [Fact]
    public async Task GetAdminFuelTypeById_ShouldReturnNull_WhenMissing()
    {
        var handler = new GetAdminFuelTypeByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminFuelTypeByIdQuery("missing"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminPackages_ShouldOrderByStationIdThenFuelNameThenLiters()
    {
        _context.FuelPackages.AddRange(
            CreatePackage("pkg-1", "station-b", "ft-b", "B Fuel", 50m),
            CreatePackage("pkg-2", "station-a", "ft-a", "A Fuel", 20m),
            CreatePackage("pkg-3", "station-a", "ft-a", "A Fuel", 10m));
        await _context.SaveChangesAsync();

        var handler = new GetAdminPackagesQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminPackagesQuery());

        result.Select(p => p.Id).Should().Equal("pkg-3", "pkg-2", "pkg-1");
    }

    [Fact]
    public async Task GetAdminPackagesByStation_ShouldFilterByStationId()
    {
        _context.FuelPackages.AddRange(
            CreatePackage("pkg-1", "station-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-2", "station-a", "ft-a", "A-95", 20m),
            CreatePackage("pkg-3", "station-b", "ft-b", "Diesel", 20m));
        await _context.SaveChangesAsync();

        var handler = new GetAdminPackagesByStationQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminPackagesByStationQuery("station-a"));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.StationId == "station-a");
    }

    [Fact]
    public async Task GetAdminStationById_ShouldReturnFound()
    {
        _context.Stations.Add(CreateStation("station-a", "Alpha Station"));
        await _context.SaveChangesAsync();

        var handler = new GetAdminStationByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminStationByIdQuery("station-a"));

        result.Should().NotBeNull();
        result!.Name.Should().Be("Alpha Station");
    }

    [Fact]
    public async Task GetAdminStationById_ShouldReturnNull_WhenMissing()
    {
        var handler = new GetAdminStationByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminStationByIdQuery("missing"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPackageSuggestions_ShouldReturnSuggestionForVoucherCombo()
    {
        _context.Stations.Add(CreateStation("okko", "OKKO"));
        _context.FuelTypes.Add(CreateFuelType("okko-95", "A-95", "okko"));
        _context.FuelVouchers.Add(CreateVoucher("OKKO", "okko-95", 50m));
        await _context.SaveChangesAsync();

        var handler = new GetPackageSuggestionsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPackageSuggestionsQuery());

        var suggestion = result.Single();
        suggestion.SuggestedId.Should().Be("okko-a-95-50");
        suggestion.StationId.Should().Be("okko");
        suggestion.StationName.Should().Be("OKKO");
        suggestion.FuelTypeId.Should().Be("okko-95");
        suggestion.FuelName.Should().Be("A-95");
        suggestion.Liters.Should().Be(50m);
    }

    [Fact]
    public async Task GetPackageSuggestions_ShouldMatchProviderCaseAndWhitespace()
    {
        _context.Stations.Add(CreateStation("okko", "OKKO"));
        _context.FuelTypes.Add(CreateFuelType("okko-95", "A-95", "okko"));
        _context.FuelVouchers.Add(CreateVoucher("  okko ", "okko-95", 50m));
        await _context.SaveChangesAsync();

        var handler = new GetPackageSuggestionsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPackageSuggestionsQuery());

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPackageSuggestions_ShouldSkipWhenPackageAlreadyExists()
    {
        _context.Stations.Add(CreateStation("okko", "OKKO"));
        _context.FuelTypes.Add(CreateFuelType("okko-95", "A-95", "okko"));
        _context.FuelPackages.Add(CreatePackage("pkg-1", "okko", "okko-95", "A-95", 50m));
        _context.FuelVouchers.Add(CreateVoucher("OKKO", "okko-95", 50m));
        await _context.SaveChangesAsync();

        var handler = new GetPackageSuggestionsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPackageSuggestionsQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPackageSuggestions_ShouldSkipWhenProviderDoesNotMatchStation()
    {
        _context.Stations.Add(CreateStation("okko", "OKKO"));
        _context.FuelTypes.Add(CreateFuelType("okko-95", "A-95", "okko"));
        _context.FuelVouchers.Add(CreateVoucher("WOG", "okko-95", 50m));
        await _context.SaveChangesAsync();

        var handler = new GetPackageSuggestionsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPackageSuggestionsQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPackageSuggestions_ShouldGroupDuplicateVoucherCombos()
    {
        _context.Stations.Add(CreateStation("okko", "OKKO"));
        _context.FuelTypes.Add(CreateFuelType("okko-95", "A-95", "okko"));
        _context.FuelVouchers.AddRange(
            CreateVoucher("OKKO", "okko-95", 50m),
            CreateVoucher("OKKO", "okko-95", 50m));
        await _context.SaveChangesAsync();

        var handler = new GetPackageSuggestionsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPackageSuggestionsQuery());

        result.Should().ContainSingle();
    }

    private static Station CreateStation(string id, string name)
    {
        return new Station
        {
            Id = id,
            Name = name,
            LogoText = "LOGO"
        };
    }

    private static StationNode CreateStationNode(string id, string stationId, string name)
    {
        return new StationNode
        {
            Id = id,
            StationId = stationId,
            Name = name
        };
    }

    private static FuelTypeEntity CreateFuelType(string id, string name, string stationId)
    {
        return new FuelTypeEntity
        {
            Id = id,
            Name = name,
            StationId = stationId,
            BasePrice = 50,
            DiscountPrice = 45
        };
    }

    private static FuelPackage CreatePackage(string id, string stationId, string fuelTypeId, string fuelName, decimal liters)
    {
        return new FuelPackage
        {
            Id = id,
            StationId = stationId,
            FuelTypeId = fuelTypeId,
            FuelName = fuelName,
            Liters = liters,
            Price = 500,
            OriginalPrice = 480
        };
    }

    private static FuelVoucher CreateVoucher(string provider, string fuelTypeId, decimal liters)
    {
        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"{provider}-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }
}
