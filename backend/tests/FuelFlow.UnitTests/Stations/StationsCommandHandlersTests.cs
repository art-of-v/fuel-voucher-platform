using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Brands.CreateBrand;
using FuelFlow.Features.Brands.DeleteBrand;
using FuelFlow.Features.Brands.UpdateBrand;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Brands;

public sealed class BrandsCommandHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    /// <summary>
    /// A brand that is not one of the seeded four, so a test cannot accidentally assert against real
    /// catalogue data.
    /// </summary>
    private static readonly Guid TestBrand = new("0a0a0a0a-0000-4000-8000-000000000001");

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
        var Brand = CreateBrand(TestBrand, "Test Brand", "#00ff80", "TS");

        var handler = new CreateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateBrandCommand(Brand));

        result.Success.Should().BeTrue();
        result.Conflict.Should().BeFalse();
        result.Error.Should().BeNull();
        result.Brand.Should().BeSameAs(Brand);

        // The id is the server's, not the caller's: an id a client chose is an id nothing else can
        // vouch for, so the row is looked up by the one the handler issued.
        result.Brand!.Id.Should().NotBe(TestBrand);
        result.Brand.Slug.Should().Be("test-brand");

        var persisted = await _context.Brands.FindAsync(result.Brand.Id);
        persisted.Should().NotBeNull();
        persisted!.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateBrand_ShouldReturnConflict_WhenSlugExists()
    {
        var Brand = CreateBrand(TestBrand, "Test Brand", "#00ff80", "TS");
        _context.Brands.Add(Brand);
        await _context.SaveChangesAsync();

        // A second row under a fresh id but the network's own code: the slug is what an operator and
        // an import file both mean by "this network", so that is the collision that has to be caught.
        var duplicate = CreateBrand(Guid.NewGuid(), "Another Name", "#00ff80", "AN", slug: "test-brand");
        var handler = new CreateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateBrandCommand(duplicate));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        result.Brand.Should().BeNull();
    }

    [Fact]
    public async Task CreateBrand_ShouldReturnError_WhenRequiredFieldsMissing()
    {
        var Brand = CreateBrand(Guid.Empty, "", "#00ff80", "");

        var handler = new CreateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new CreateBrandCommand(Brand));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeFalse();
        result.Error.Should().Be("Slug, Name and LogoText are required");
    }

    [Fact]
    public async Task UpdateBrand_ShouldUpdateFields()
    {
        var Brand = CreateBrand(TestBrand, "Old Name", "#000000", "OLD");
        _context.Brands.Add(Brand);
        await _context.SaveChangesAsync();

        var updated = new Brand
        {
            Id = TestBrand,
            Name = "New Name",
            Color = "#ffffff",
            LogoText = "NEW",
        };
        var handler = new UpdateBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdateBrandCommand(TestBrand, updated));

        result.Should().BeTrue();

var persisted = await _context.Brands.FindAsync(TestBrand);
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

        var result = await handler.HandleAsync(new UpdateBrandCommand(BrandIds.NotFound, CreateBrand(BrandIds.NotFound, "X")));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBrand_ShouldRemove()
    {
        var Brand = CreateBrand(TestBrand, "Test Brand", "#00ff80", "TS");
        _context.Brands.Add(Brand);
        await _context.SaveChangesAsync();

        var handler = new DeleteBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteBrandCommand(TestBrand));

        result.Should().BeTrue();
        var persisted = await _context.Brands.IgnoreQueryFilters().SingleAsync(s => s.Id == TestBrand);
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAtUtc.Should().NotBeNull();
        (await _context.Brands.CountAsync(s => s.Id == TestBrand)).Should().Be(0,
            "#217: a delete archives rather than removes - hidden from the catalog, row intact");
    }

    [Fact]
    public async Task DeleteBrand_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new DeleteBrandCommandHandler(_context);

        var result = await handler.HandleAsync(new DeleteBrandCommand(BrandIds.NotFound));

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

    private static Brand CreateBrand(Guid id, string name, string color = "#00ff80", string logoText = "LOGO", string? slug = null)
    {
        return new Brand
        {
            Id = id,
            Name = name,
            // The slug is the network's own code and is unique, so a fixture states one even when the
            // test never looks at it - the alternative is a brand row the database would refuse.
            Slug = slug ?? name.ToLowerInvariant().Replace(' ', '-'),
            Color = color,
            LogoText = logoText
        };
    }

    private static FuelTypeEntity CreateFuelType(string id, string name, Guid BrandId, int basePrice = 50, int discountPrice = 45)
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
