using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Brands.CreateFuelType;
using FuelFlow.Features.Brands.CreateBrand;
using FuelFlow.Features.Brands.DeleteFuelType;
using FuelFlow.Features.Brands.DeleteBrand;
using FuelFlow.Features.Brands.UpdateFuelType;
using FuelFlow.Features.Brands.UpdateBrand;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Brands;

public sealed class BrandsCommandHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public BrandsCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task CreateBrand_ShouldAddBrand()
    {
        var Brand = CreateBrand("Brand-1", "Test Brand", "#00ff80", "TS");

        var handler = new CreateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateBrandCommand(Brand));

        result.Success.Should().BeTrue();
        result.Conflict.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Brand.Should().BeSameAs(Brand);

        var persisted = await _context.Brands.FindAsync("Brand-1");
        persisted.Should().NotBeNull();
        persisted!.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateBrand_ShouldReturnConflict_WhenIdExists()
    {
        var Brand = CreateBrand("Brand-1", "Test Brand", "#00ff80", "TS");
        _context.Brands.Add(Brand);
        await _context.SaveChangesAsync();

        var handler = new CreateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateBrandCommand(Brand));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        result.Brand.Should().BeNull();
    }

    [Fact]
    public async Task CreateBrand_ShouldReturnError_WhenRequiredFieldsMissing()
    {
        var Brand = CreateBrand("", "", "#00ff80", "");

        var handler = new CreateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateBrandCommand(Brand));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeFalse();
        result.Error.Should().Be("Id, Name and LogoText are required");
    }

    [Fact]
    public async Task UpdateBrand_ShouldUpdateFields()
    {
        var Brand = CreateBrand("Brand-1", "Old Name", "#000000", "OLD");
        _context.Brands.Add(Brand);
        await _context.SaveChangesAsync();

        var updated = new Brand
        {
            Id = "Brand-1",
            Name = "New Name",
            Color = "#ffffff",
            LogoText = "NEW",
        };
        var handler = new UpdateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateBrandCommand("Brand-1", updated));

        result.Should().BeTrue();

var persisted = await _context.Brands.FindAsync("Brand-1");
        persisted!.Name.Should().Be("New Name");
        persisted.Color.Should().Be("#ffffff");
        persisted.LogoText.Should().Be("NEW");
        // A brand is a network, not a place: address, phone, station type and coordinates moved to
        // brand_stations, so an update to one no longer carries them and cannot write a stray address
        // onto the brand row.
    }

    [Fact]
    public async Task UpdateBrand_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new UpdateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateBrandCommand("missing", CreateBrand("missing", "X")));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBrand_ShouldRemove()
    {
        var Brand = CreateBrand("Brand-1", "Test Brand", "#00ff80", "TS");
        _context.Brands.Add(Brand);
        await _context.SaveChangesAsync();

        var handler = new DeleteBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteBrandCommand("Brand-1"));

        result.Should().BeTrue();
        var persisted = await _context.Brands.IgnoreQueryFilters().SingleAsync(s => s.Id == "Brand-1");
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAtUtc.Should().NotBeNull();
        (await _context.Brands.CountAsync(s => s.Id == "Brand-1")).Should().Be(0,
            "#217: a delete archives rather than removes - hidden from the catalog, row intact");
    }

    [Fact]
    public async Task DeleteBrand_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new DeleteBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteBrandCommand("missing"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateFuelType_ShouldUpdate()
    {
        var fuelType = CreateFuelType("ft-1", "A-95", "Brand-1", 50, 48);
        _context.FuelTypes.Add(fuelType);
        await _context.SaveChangesAsync();

        var updated = CreateFuelType("ft-1", "A-95 Euro", "Brand-2", 60, 55);
        var handler = new UpdateFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateFuelTypeCommand("ft-1", updated, Guid.NewGuid()));

        result.Should().BeTrue();

        var persisted = await _context.FuelTypes.FindAsync("ft-1");
        persisted!.Name.Should().Be("A-95 Euro");
        persisted.BrandId.Should().Be("Brand-2");
        persisted.BasePrice.Should().Be(60);
        persisted.DiscountPrice.Should().Be(55);
    }

    [Fact]
    public async Task UpdateFuelType_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new UpdateFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateFuelTypeCommand("missing", CreateFuelType("missing", "X", "Brand-1"), Guid.NewGuid()));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFuelType_ShouldRemove()
    {
        var fuelType = CreateFuelType("ft-1", "A-95", "Brand-1");
        _context.FuelTypes.Add(fuelType);
        await _context.SaveChangesAsync();

        var handler = new DeleteFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteFuelTypeCommand("ft-1"));

        result.Should().BeTrue();
        var persisted = await _context.FuelTypes.IgnoreQueryFilters().SingleAsync(f => f.Id == "ft-1");
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAtUtc.Should().NotBeNull();
        (await _context.FuelTypes.CountAsync(f => f.Id == "ft-1")).Should().Be(0,
            "#217: archiving is what keeps the vouchers sold against this fuel alive");
    }

    [Fact]
    public async Task DeleteFuelType_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new DeleteFuelTypeCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteFuelTypeCommand("missing"));

        result.Should().BeFalse();
    }

    // The incident this guards: a 0.01 L test package priced at 5.00/L was added inside a live fuel
    // whose other packages cost 100.00/L. Nothing complained, the fuel card kept reporting the fuel as
    // healthy, and the below-cost guard then refused to sell the real fuel at all. Every other write
    // path copies the cost off an existing package, so this endpoint was the only way in.

    // The column is numeric(…, 4): a value that only differs past that scale is the same price, and a
    // refusal over it would teach operators to ignore the guard.

    // "Never recorded" is not a disagreement: the pricing engine already skips null-cost rows, and
    // refusing here would block the ordinary case of pricing a fuel in two steps.

    private static Brand CreateBrand(string id, string name, string color = "#00ff80", string logoText = "LOGO")
    {
        return new Brand
        {
            Id = id,
            Name = name,
            Color = color,
            LogoText = logoText
        };
    }

    private static FuelTypeEntity CreateFuelType(string id, string name, string BrandId, int basePrice = 50, int discountPrice = 45)
    {
        return new FuelTypeEntity
        {
            Id = id,
            Name = name,
            BrandId = BrandId,
            BasePrice = basePrice,
            DiscountPrice = discountPrice
        };
    }

}
