using FluentAssertions;
using FuelFlow.Features.Stations.CreateFuelType;
using FuelFlow.Features.Stations.CreatePackage;
using FuelFlow.Features.Stations.CreateStation;
using FuelFlow.Features.Stations.DeleteFuelType;
using FuelFlow.Features.Stations.DeletePackage;
using FuelFlow.Features.Stations.DeleteStation;
using FuelFlow.Features.Stations.UpdateFuelType;
using FuelFlow.Features.Stations.UpdatePackage;
using FuelFlow.Features.Stations.UpdateStation;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Stations;

public sealed class StationsCommandHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public StationsCommandHandlersTests()
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
    public async Task CreateStation_ShouldAddStation()
    {
        var station = CreateStation("station-1", "Test Station", "#00ff80", "TS");

        var handler = new CreateStationCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateStationCommand(station));

        result.Success.Should().BeTrue();
        result.Conflict.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Station.Should().BeSameAs(station);

        var persisted = await _context.Stations.FindAsync("station-1");
        persisted.Should().NotBeNull();
        persisted!.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateStation_ShouldReturnConflict_WhenIdExists()
    {
        var station = CreateStation("station-1", "Test Station", "#00ff80", "TS");
        _context.Stations.Add(station);
        await _context.SaveChangesAsync();

        var handler = new CreateStationCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateStationCommand(station));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        result.Station.Should().BeNull();
    }

    [Fact]
    public async Task CreateStation_ShouldReturnError_WhenRequiredFieldsMissing()
    {
        var station = CreateStation("", "", "#00ff80", "");

        var handler = new CreateStationCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateStationCommand(station));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeFalse();
        result.Error.Should().Be("Id, Name and LogoText are required");
    }

    [Fact]
    public async Task UpdateStation_ShouldUpdateFields()
    {
        var station = CreateStation("station-1", "Old Name", "#000000", "OLD");
        _context.Stations.Add(station);
        await _context.SaveChangesAsync();

        var updated = new Station
        {
            Id = "station-1",
            Name = "New Name",
            Color = "#ffffff",
            LogoText = "NEW",
            Address = "New Address",
            Phone = "123456789",
            StationType = "fuel",
            Lat = 50.5,
            Lng = 30.5
        };
        var handler = new UpdateStationCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateStationCommand("station-1", updated));

        result.Should().BeTrue();

        var persisted = await _context.Stations.FindAsync("station-1");
        persisted!.Name.Should().Be("New Name");
        persisted.Color.Should().Be("#ffffff");
        persisted.LogoText.Should().Be("NEW");
        persisted.Address.Should().Be("New Address");
        persisted.Phone.Should().Be("123456789");
        persisted.StationType.Should().Be("fuel");
        persisted.Lat.Should().Be(50.5);
        persisted.Lng.Should().Be(30.5);
    }

    [Fact]
    public async Task UpdateStation_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new UpdateStationCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateStationCommand("missing", CreateStation("missing", "X")));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteStation_ShouldRemove()
    {
        var station = CreateStation("station-1", "Test Station", "#00ff80", "TS");
        _context.Stations.Add(station);
        await _context.SaveChangesAsync();

        var handler = new DeleteStationCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteStationCommand("station-1"));

        result.Should().BeTrue();
        var persisted = await _context.Stations.FindAsync("station-1");
        persisted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteStation_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new DeleteStationCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteStationCommand("missing"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateFuelType_ShouldAdd()
    {
        var fuelType = CreateFuelType("ft-1", "A-95", "station-1");

        var handler = new CreateFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateFuelTypeCommand(fuelType));

        result.Success.Should().BeTrue();
        result.Conflict.Should().BeFalse();
        result.Error.Should().BeNull();
        result.FuelType.Should().BeSameAs(fuelType);

        var persisted = await _context.FuelTypes.FindAsync("ft-1");
        persisted.Should().NotBeNull();
        persisted!.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateFuelType_ShouldReturnConflict_WhenExists()
    {
        var fuelType = CreateFuelType("ft-1", "A-95", "station-1");
        _context.FuelTypes.Add(fuelType);
        await _context.SaveChangesAsync();

        var handler = new CreateFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateFuelTypeCommand(fuelType));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        result.FuelType.Should().BeNull();
    }

    [Fact]
    public async Task CreateFuelType_ShouldReturnError_WhenRequiredFieldsMissing()
    {
        var fuelType = CreateFuelType("", "", "");

        var handler = new CreateFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateFuelTypeCommand(fuelType));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeFalse();
        result.Error.Should().Be("Id, StationId and Name are required");
    }

    [Fact]
    public async Task UpdateFuelType_ShouldUpdate()
    {
        var fuelType = CreateFuelType("ft-1", "A-95", "station-1", 50, 48);
        _context.FuelTypes.Add(fuelType);
        await _context.SaveChangesAsync();

        var updated = CreateFuelType("ft-1", "A-95 Euro", "station-2", 60, 55);
        var handler = new UpdateFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateFuelTypeCommand("ft-1", updated, Guid.NewGuid()));

        result.Should().BeTrue();

        var persisted = await _context.FuelTypes.FindAsync("ft-1");
        persisted!.Name.Should().Be("A-95 Euro");
        persisted.StationId.Should().Be("station-2");
        persisted.BasePrice.Should().Be(60);
        persisted.DiscountPrice.Should().Be(55);
    }

    [Fact]
    public async Task UpdateFuelType_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new UpdateFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateFuelTypeCommand("missing", CreateFuelType("missing", "X", "station-1"), Guid.NewGuid()));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFuelType_ShouldRemove()
    {
        var fuelType = CreateFuelType("ft-1", "A-95", "station-1");
        _context.FuelTypes.Add(fuelType);
        await _context.SaveChangesAsync();

        var handler = new DeleteFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteFuelTypeCommand("ft-1"));

        result.Should().BeTrue();
        var persisted = await _context.FuelTypes.FindAsync("ft-1");
        persisted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteFuelType_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new DeleteFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteFuelTypeCommand("missing"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreatePackage_ShouldAdd()
    {
        var package = CreatePackage("pkg-1", "station-1", "ft-1", "A-95", 20m, 1000, 980);

        var handler = new CreatePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new CreatePackageCommand(package));

        result.Success.Should().BeTrue();
        result.Conflict.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Package.Should().BeSameAs(package);

        var persisted = await _context.FuelPackages.FindAsync("pkg-1");
        persisted.Should().NotBeNull();
        persisted!.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreatePackage_ShouldReturnConflict_WhenExists()
    {
        var package = CreatePackage("pkg-1", "station-1", "ft-1", "A-95", 20m, 1000, 980);
        _context.FuelPackages.Add(package);
        await _context.SaveChangesAsync();

        var handler = new CreatePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new CreatePackageCommand(package));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        result.Package.Should().BeNull();
    }

    [Fact]
    public async Task CreatePackage_ShouldReturnError_WhenRequiredFieldsMissing()
    {
        var package = CreatePackage("", "", "", "A-95", 20m);

        var handler = new CreatePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new CreatePackageCommand(package));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeFalse();
        result.Error.Should().Be("Id, StationId and FuelTypeId are required");
    }

    [Fact]
    public async Task UpdatePackage_ShouldUpdate()
    {
        var package = CreatePackage("pkg-1", "station-1", "ft-1", "A-95", 20m, 1000, 980);
        _context.FuelPackages.Add(package);
        await _context.SaveChangesAsync();

        var updated = CreatePackage("pkg-1", "station-2", "ft-2", "Diesel", 50m, 3000, 2900);
        var handler = new UpdatePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdatePackageCommand("pkg-1", updated));

        result.Should().BeTrue();

        var persisted = await _context.FuelPackages.FindAsync("pkg-1");
        persisted!.StationId.Should().Be("station-2");
        persisted.FuelTypeId.Should().Be("ft-2");
        persisted.FuelName.Should().Be("Diesel");
        persisted.Liters.Should().Be(50m);
        persisted.Price.Should().Be(3000);
        persisted.OriginalPrice.Should().Be(2900);
    }

    [Fact]
    public async Task UpdatePackage_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new UpdatePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdatePackageCommand("missing", CreatePackage("missing", "station-1", "ft-1", "A-95", 20m)));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeletePackage_ShouldRemove()
    {
        var package = CreatePackage("pkg-1", "station-1", "ft-1", "A-95", 20m);
        _context.FuelPackages.Add(package);
        await _context.SaveChangesAsync();

        var handler = new DeletePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new DeletePackageCommand("pkg-1"));

        result.Should().BeTrue();
        var persisted = await _context.FuelPackages.FindAsync("pkg-1");
        persisted.Should().BeNull();
    }

    [Fact]
    public async Task DeletePackage_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new DeletePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new DeletePackageCommand("missing"));

        result.Should().BeFalse();
    }

    private static Station CreateStation(string id, string name, string color = "#00ff80", string logoText = "LOGO")
    {
        return new Station
        {
            Id = id,
            Name = name,
            Color = color,
            LogoText = logoText
        };
    }

    private static FuelTypeEntity CreateFuelType(string id, string name, string stationId, int basePrice = 50, int discountPrice = 45)
    {
        return new FuelTypeEntity
        {
            Id = id,
            Name = name,
            StationId = stationId,
            BasePrice = basePrice,
            DiscountPrice = discountPrice
        };
    }

    private static FuelPackage CreatePackage(string id, string stationId, string fuelTypeId, string fuelName, decimal liters, int price = 500, int originalPrice = 480)
    {
        return new FuelPackage
        {
            Id = id,
            StationId = stationId,
            FuelTypeId = fuelTypeId,
            FuelName = fuelName,
            Liters = liters,
            Price = price,
            OriginalPrice = originalPrice
        };
    }
}
