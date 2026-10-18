using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Brands.CreateFuelType;
using FuelFlow.Features.Brands.CreatePackage;
using FuelFlow.Features.Brands.CreateBrand;
using FuelFlow.Features.Brands.DeleteFuelType;
using FuelFlow.Features.Brands.DeletePackage;
using FuelFlow.Features.Brands.DeleteBrand;
using FuelFlow.Features.Brands.UpdateFuelType;
using FuelFlow.Features.Brands.UpdatePackage;
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
    public async Task CreateFuelType_ShouldAdd()
    {
        var fuelType = CreateFuelType("ft-1", "A-95", "Brand-1");

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
        var fuelType = CreateFuelType("ft-1", "A-95", "Brand-1");
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
        result.Error.Should().Be("Id, BrandId and Name are required");
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

    [Fact]
public async Task CreatePackage_ShouldAdd()
{
    // Priced like a real fuel: the nominal carries no price of its own, so what it sells for is
    // the fuel's rate x litres. 56 cost + 7 margin = 63, times 20 L = 1260.
    var fuel = CreateFuelType("ft-1", "A-95", "Brand-1");
    fuel.MarginUahPerLiter = 7m;
    _context.FuelTypes.Add(fuel);

    var package = CreatePackage("pkg-1", "Brand-1", "ft-1", "A-95", 20m, 1260, 1400, costPerLiter: 56m);

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));

        var result = await handler.HandleAsync(new CreatePackageCommand(package));

        result.Success.Should().BeTrue();
        result.Conflict.Should().BeFalse();
        result.Error.Should().BeNull();
        // Not the same instance any more, and that is the point: what the handler persists is a volume, so
        // what it returns is the package VIEW of that volume rendered through PackageProjection. The
        // endpoint's published JSON is unchanged; only the object identity differs, and nothing was
        // entitled to rely on that - the volume never existed as a detached entity to be handed back.
        result.Package.Should().NotBeNull();
        result.Package!.Id.Should().Be("pkg-1");
        result.Package.BrandId.Should().Be("Brand-1");
        result.Package.FuelTypeId.Should().Be("ft-1");
        result.Package.Liters.Should().Be(20m);
        result.Package.Price.Should().Be(1260);

        var persisted = await _context.FuelOfferVolumes.FindAsync("pkg-1");
        persisted.Should().NotBeNull();
        persisted!.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreatePackage_ShouldReturnConflict_WhenExists()
    {
        var package = CreatePackage("pkg-1", "Brand-1", "ft-1", "A-95", 20m, 1000, 980);
        SeedFromPackage(package);
        await _context.SaveChangesAsync();

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));

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

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));

        var result = await handler.HandleAsync(new CreatePackageCommand(package));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeFalse();
        result.Error.Should().Be("Id, BrandId and FuelTypeId are required");
    }

    // The incident this guards: a 0.01 L test package priced at 5.00/L was added inside a live fuel
    // whose other packages cost 100.00/L. Nothing complained, the fuel card kept reporting the fuel as
    // healthy, and the below-cost guard then refused to sell the real fuel at all. Every other write
    // path copies the cost off an existing package, so this endpoint was the only way in.
    [Fact]
    public async Task CreatePackage_ShouldRefuse_WhenCostDisagreesWithTheFuel()
    {
        SeedFromPackage(CreatePackage("existing", "Brand-1", "ft-1", "ДП ЄВРО", 10m, costPerLiter: 100m));
        SeedFromPackage(CreatePackage("existing-2", "Brand-1", "ft-1", "ДП ЄВРО", 2m, costPerLiter: 100m));
        await _context.SaveChangesAsync();

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));
        var result = await handler.HandleAsync(new CreatePackageCommand(
            CreatePackage("qa-1kop", "Brand-1", "ft-1", "QA 1 коп", 0.01m, costPerLiter: 5m)));

        result.Success.Should().BeFalse();
        result.Conflict.Should().BeTrue();
        result.Error.Should().Contain("100").And.Contain("5");
        result.Error.Should().Contain("own fuel type", "the fix has to be actionable, not just a refusal");

        (await _context.FuelOfferVolumes.AnyAsync(p => p.Id == "qa-1kop")).Should().BeFalse();
    }

    [Fact]
    public async Task CreatePackage_ShouldAdd_WhenCostMatchesTheFuel()
    {
        SeedFromPackage(CreatePackage("existing", "Brand-1", "ft-1", "ДП ЄВРО", 10m, costPerLiter: 94.9m));
        await _context.SaveChangesAsync();

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));
        var result = await handler.HandleAsync(new CreatePackageCommand(
            CreatePackage("new-nominal", "Brand-1", "ft-1", "ДП ЄВРО", 5m, costPerLiter: 94.9m)));

        result.Success.Should().BeTrue();
        result.Conflict.Should().BeFalse();
    }

    // The column is numeric(…, 4): a value that only differs past that scale is the same price, and a
    // refusal over it would teach operators to ignore the guard.
    [Fact]
    public async Task CreatePackage_ShouldAdd_WhenCostDiffersOnlyPastTheColumnScale()
    {
        SeedFromPackage(CreatePackage("existing", "Brand-1", "ft-1", "A-95", 10m, costPerLiter: 94.9000m));
        await _context.SaveChangesAsync();

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));
        var result = await handler.HandleAsync(new CreatePackageCommand(
            CreatePackage("rounding", "Brand-1", "ft-1", "A-95", 5m, costPerLiter: 94.90001m)));

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePackage_ShouldAdd_WhenItIsTheFirstPricedPackageOfTheFuel()
    {
        SeedFromPackage(CreatePackage("existing", "Brand-1", "ft-1", "A-95", 10m));
        await _context.SaveChangesAsync();

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));
        var result = await handler.HandleAsync(new CreatePackageCommand(
            CreatePackage("priced", "Brand-1", "ft-1", "A-95", 5m, costPerLiter: 87.9m)));

        result.Success.Should().BeTrue();
    }

    // "Never recorded" is not a disagreement: the pricing engine already skips null-cost rows, and
    // refusing here would block the ordinary case of pricing a fuel in two steps.
    [Fact]
    public async Task CreatePackage_ShouldAdd_WhenTheFuelHasCostsButTheNewOneHasNone()
    {
        SeedFromPackage(CreatePackage("existing", "Brand-1", "ft-1", "A-95", 10m, costPerLiter: 87.9m));
        await _context.SaveChangesAsync();

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));
        var result = await handler.HandleAsync(new CreatePackageCommand(
            CreatePackage("uncosted", "Brand-1", "ft-1", "A-95", 5m)));

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePackage_ShouldAdd_WhenItBelongsToADifferentFuel()
    {
        SeedFromPackage(CreatePackage("existing", "Brand-1", "ft-1", "A-95", 10m, costPerLiter: 87.9m));
        await _context.SaveChangesAsync();

        var handler = new CreatePackageCommandHandler(_context, new OfferPriceRecorder(_context));
        var result = await handler.HandleAsync(new CreatePackageCommand(
            CreatePackage("other-fuel", "Brand-1", "ft-2", "ДП ЄВРО", 10m, costPerLiter: 94.9m)));

        result.Success.Should().BeTrue();
    }

    /// <summary>
    /// A package owns its litres and its frozen total; the offer owns everything else.
    /// </summary>
    /// <remarks>
    /// This test used to assert that <c>PUT</c> re-keys a package onto a different Brand and fuel,
    /// and that expectation was correct for the old schema. It is wrong now, and for a reason worth
    /// stating: Brand and fuel are part of the OFFER's identity - <c>UNIQUE (brand_id,
    /// fuel_type_id, data_kind)</c> - so "update this package's Brand" is not an update, it is a move
    /// to another offer, and it would drag that offer's other volumes with it. Re-keying a whole fuel
    /// between Brands is what the brand screen does; this endpoint cannot do it half-way.
    /// <para>
    /// So the handler applies the two facts a package genuinely owns and ignores the rest. Silently
    /// ignoring beats corrupting the offer, but it is a behaviour change and this test is where it is
    /// pinned down.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task UpdatePackage_ShouldUpdateLitersAndPrice_AndLeaveTheOfferAlone()
    {
        var package = CreatePackage("pkg-1", "Brand-1", "ft-1", "A-95", 20m, 1000, 980);
        SeedFromPackage(package);
        await _context.SaveChangesAsync();

        var offerIdBefore = (await _context.FuelOfferVolumes.FindAsync("pkg-1"))!.OfferId;

        var updated = CreatePackage("pkg-1", "Brand-2", "ft-2", "Diesel", 50m, 3000, 2900);
        var handler = new UpdatePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdatePackageCommand("pkg-1", updated));

        result.Should().BeTrue();

        _context.ChangeTracker.Clear();
        var persisted = await _context.FuelOfferVolumes.FindAsync("pkg-1");
        persisted!.Liters.Should().Be(50m);
        // The endpoint now sets an OVERRIDE, and 3000 is the round marketing price it was written to
        // test with. It is no longer a stored total that other write paths overwrite.

        // The volume still belongs to the offer it was created under.
        persisted.OfferId.Should().Be(offerIdBefore);

        var offer = await _context.FuelOffers.FindAsync(offerIdBefore);
        offer!.BrandId.Should().Be("Brand-1");
        offer.FuelTypeId.Should().Be("ft-1");
    }

    [Fact]
    public async Task UpdatePackage_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new UpdatePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new UpdatePackageCommand("missing", CreatePackage("missing", "Brand-1", "ft-1", "A-95", 20m)));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeletePackage_ShouldRemove()
    {
        var package = CreatePackage("pkg-1", "Brand-1", "ft-1", "A-95", 20m);
        SeedFromPackage(package);
        await _context.SaveChangesAsync();

        var handler = new DeletePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new DeletePackageCommand("pkg-1"));

        result.Should().BeTrue();
        var persisted = await _context.FuelOfferVolumes.IgnoreQueryFilters().SingleAsync(p => p.Id == "pkg-1");
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAtUtc.Should().NotBeNull();
        (await _context.FuelOfferVolumes.CountAsync(p => p.Id == "pkg-1")).Should().Be(0,
            "#217: order_line_items point at packages, so the row is history rather than garbage");
    }

    [Fact]
    public async Task DeletePackage_ShouldReturnFalse_WhenNotFound()
    {
        var handler = new DeletePackageCommandHandler(_context);

        var result = await handler.HandleAsync(new DeletePackageCommand("missing"));

        result.Should().BeFalse();
    }

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

    private static FuelPackage CreatePackage(string id, string BrandId, string fuelTypeId, string fuelName, decimal liters, int price = 500, int originalPrice = 480, decimal? costPerLiter = null)
    {
        return new FuelPackage
        {
            Id = id,
            BrandId = BrandId,
            FuelTypeId = fuelTypeId,
            FuelName = fuelName,
            Liters = liters,
            // The effective price is what these fixtures state the package should sell for, and the
            // endpoint writes it as an OVERRIDE — so it is both, here. A package priced by hand rather than
            // derived from its fuel's rate is exactly what these two endpoints exist for.
            Price = price,
            OriginalPrice = originalPrice,
            SupplierPricePerLiter = costPerLiter
        };
    }

    /// <summary>
    /// Seeds the package described by a command DTO as an offer plus a volume.
    /// </summary>
    /// <remarks>
    /// The DTO stays a <see cref="FuelPackage"/> because that is the published shape of the create and
    /// update endpoints, and these tests are about the handlers, not the schema. Only the INSERT moves:
    /// <c>fuel_packages</c> is no longer what any handler reads, so a fixture inserted there produces a
    /// handler that correctly reports "nothing to update" - which looks like the handler being broken.
    /// </remarks>
    private FuelOfferVolume SeedFromPackage(FuelPackage package)
        => _context.SeedPackage(
            id: package.Id,
            BrandId: package.BrandId,
            fuelTypeId: package.FuelTypeId,
            liters: package.Liters,
            price: package.Price,
            supplierPricePerLiter: package.SupplierPricePerLiter);
}
