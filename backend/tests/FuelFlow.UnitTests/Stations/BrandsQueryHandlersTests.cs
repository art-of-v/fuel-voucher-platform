using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.GetAdminFuelTypeById;
using FuelFlow.Features.Brands.GetAdminFuelTypes;
using FuelFlow.Features.Brands.GetAdminBrandById;
using FuelFlow.Features.Brands.GetAdminBrands;
using FuelFlow.Features.Brands.GetPublicPackages;
using FuelFlow.Features.Brands.GetPublicPackagesByBrand;
using FuelFlow.Features.Brands.GetPublicBrandStations;
using FuelFlow.Features.Brands.GetPublicBrandStationsByBrand;
using FuelFlow.Features.Brands.GetPublicBrands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Availability;
using Microsoft.Extensions.Configuration;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Brands;

public sealed class BrandsQueryHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public BrandsQueryHandlersTests()
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
    public async Task GetPublicBrands_ShouldReturnSeededBrands()
    {
        _context.Brands.AddRange(
            CreateBrand("Brand-b", "Beta Brand"),
            CreateBrand("Brand-a", "Alpha Brand"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicBrandsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicBrandsQuery());

        result.Should().HaveCount(2);
        result.Select(s => s.Id).Should().Contain(new[] { "Brand-a", "Brand-b" });
        result[0].Name.Should().Be("Alpha Brand");
        result[1].Name.Should().Be("Beta Brand");
    }

    [Fact]
    public async Task GetPublicBrands_ShouldReturnEmptyList_WhenNoBrands()
    {
        var handler = new GetPublicBrandsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicBrandsQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAdminBrands_ShouldPageResults()
    {
        for (var i = 1; i <= 5; i++)
        {
            _context.Brands.Add(CreateBrand($"Brand-{i}", $"Brand {i}"));
        }
        await _context.SaveChangesAsync();

        var handler = new GetAdminBrandsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminBrandsQuery(Page: 1, PageSize: 2));

        result.Items.Should().HaveCount(2);
        result.Items.Select(s => s.Id).Should().Equal("Brand-1", "Brand-2");
        result.TotalCount.Should().Be(5);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.TotalPages.Should().Be(3);
        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdminBrands_ShouldReturnLaterPages()
    {
        for (var i = 1; i <= 5; i++)
        {
            _context.Brands.Add(CreateBrand($"Brand-{i}", $"Brand {i}"));
        }
        await _context.SaveChangesAsync();

        var handler = new GetAdminBrandsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminBrandsQuery(Page: 3, PageSize: 2));

        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be("Brand-5");
        result.HasPreviousPage.Should().BeTrue();
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicBrandStations_ShouldReturnAllNodes()
    {
        _context.BrandStations.AddRange(
CreateBrandStation("node-1", "Brand-a", "Node A"),
                CreateBrandStation("node-2", "Brand-b", "Node B"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicBrandStationsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicBrandStationsQuery());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPublicBrandStationsByBrand_ShouldFilterByBrandId()
    {
        _context.BrandStations.AddRange(
            CreateBrandStation("node-1", "Brand-a", "Node A1"),
            CreateBrandStation("node-2", "Brand-a", "Node A2"),
            CreateBrandStation("node-3", "Brand-b", "Node B"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicBrandStationsByBrandQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicBrandStationsByBrandQuery("Brand-a"));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(n => n.BrandId == "Brand-a");
    }

    [Fact]
    public async Task GetPublicPackages_ShouldReturnAllPackages()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-1", "Brand-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-2", "Brand-b", "ft-b", "Diesel", 20m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().HaveCount(2);
    }

    /// <summary>
    /// Production served a QA fuel type as a live package - "QA-CHECK-95", 5 L, 1 UAH - through this
    /// anonymous endpoint. The rows are gone; this is what stops the next one being purchasable.
    /// </summary>
    [Fact]
    public async Task GetPublicPackages_ShouldNotServeAFlaggedTestPackage()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-real", "Brand-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-qa", "Brand-a", "ft-qa", "QA-CHECK-95", 5m, isTestData: true));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle("a rehearsal package reaching the store front is purchasable");
        result[0].Id.Should().Be(PackageIds.For(OfferKeys.For("ft-a", DataKinds.Real), 10m));
    }

    /// <summary>
    /// The second anonymous route. A filter on GetPublicPackages alone would have left the
    /// rehearsal package buyable through /api/packages/Brand/{id}.
    /// </summary>
    [Fact]
    public async Task GetPublicPackagesByBrand_ShouldNotServeAFlaggedTestPackage()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-real", "Brand-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-qa", "Brand-a", "ft-qa", "QA-CHECK-95", 5m, isTestData: true));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesByBrandQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesByBrandQuery("Brand-a", CallerUserId: null));

        result.Should().ContainSingle();
        result[0].Id.Should().Be(PackageIds.For(OfferKeys.For("ft-a", DataKinds.Real), 10m));
    }

    [Fact]
    public async Task GetPublicPackagesByBrand_ShouldFilterByBrandId()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-1", "Brand-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-2", "Brand-a", "ft-a", "A-95", 20m),
            CreatePackage("pkg-3", "Brand-b", "ft-b", "Diesel", 20m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesByBrandQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesByBrandQuery("Brand-a", CallerUserId: null));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.BrandId == "Brand-a");
    }

    [Fact]
    public async Task GetPublicPackages_ShouldProjectStoreFrontFields_AndComputeOriginalFromPump()
    {
        // Seed a package with the operator's commercially-sensitive pricing populated — the public
        // endpoint must project only the store-front fields and never these (planning #52). The
        // struck-through "before" price is recomputed fresh from the pump price, so the stale
        // cost-derived OriginalPrice column is ignored and the supplier cost can never leak (#73).
// No stored "before" price any more: it was never authoritative, and the storefront recomputes it
        // from the pump price - which is what stops a stale cost-derived value reaching anonymous callers.
        _context.FuelNominals.Add(CreatePackage("pkg-1", "Brand-a", "ft-a", "A-95", 10m));
        _context.SeedOffer("Brand-a", "ft-a",
            pumpPricePerLiter: 60m,      // public list price -> fresh "before" = 600
            finalPricePerLiter: 50m,
            supplierPricePerLiter: 42m,
            marginUahPerLiter: 8m,
            marginPercent: 19m);
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        var pkg = result.Should().ContainSingle().Which;
        // The id is derived from the offer and the size, because a package is not a row any more.
        // Asserted through PackageIds rather than spelled out, so a change to the format fails here
        // instead of quietly splitting the shelf from the checkout resolver.
        pkg.Id.Should().Be(PackageIds.For(OfferKeys.For("ft-a", DataKinds.Real), 10m));
        pkg.BrandId.Should().Be("Brand-a");
        pkg.FuelTypeId.Should().Be("ft-a");
        pkg.FuelName.Should().Be("A-95");
        pkg.Liters.Should().Be(10m);
        pkg.Price.Should().Be(500);
        // Pump × liters (60 × 10), NOT the stale stored 420 nor the supplier cost.
        pkg.OriginalPrice.Should().Be(600);
        pkg.FinalPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task GetPublicPackages_NullPump_FallsBackToSalePrice_SoNoFabricatedSaving()
    {
        // Legacy fuel priced before any pump was recorded: with no pump ceiling the "before" price is
        // just the sale price, so the store-front shows no fabricated saving — and still never the
        // stored cost-derived OriginalPrice (planning #73).
// The legacy case: a package with no pump price at all. There is nothing to recompute the struck
        // price from, so the storefront falls back to the sale price and shows no fabricated saving.
        _context.FuelNominals.Add(CreatePackage("pkg-legacy", "Brand-a", "ft-a", "A-95", 10m));
        _context.SeedOffer("Brand-a", "ft-a",
            pumpPricePerLiter: null,
            finalPricePerLiter: 50m,
            supplierPricePerLiter: 42m,
            marginUahPerLiter: 8m);
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        var pkg = result.Should().ContainSingle().Which;
        pkg.OriginalPrice.Should().Be(500);   // == sale price → no saving shown
    }

    /// <summary>
    /// The whole point of the flag: the catalog keeps listing every package, and says which of them
    /// the shop can actually sell right now. A package with no activated voucher behind it is marked
    /// unavailable rather than hidden — the customer can still read the price and walk the app.
    /// </summary>
    [Fact]
    public async Task GetPublicPackages_ShouldFlagAvailability_PromotingNothingAndHidingNothing()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-in-stock", "Brand-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-empty", "Brand-a", "ft-a", "A-95", 20m));
        _context.FuelVouchers.Add(CreateVoucher("Brand-a", "ft-a", 10m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

result.Should().HaveCount(2, "an empty shelf is greyed out, not removed from the catalog");
    var inStock = PackageIds.For(OfferKeys.For("ft-a", DataKinds.Real), 10m);
    var empty = PackageIds.For(OfferKeys.For("ft-a", DataKinds.Real), 20m);
    result.Single(p => p.Id == inStock).IsAvailable.Should().BeTrue();
    result.Single(p => p.Id == empty).IsAvailable.Should().BeFalse();
}

    /// <summary>
    /// Imported stock is not sellable stock. A freshly imported batch sits in <c>Imported</c> until a
    /// manager costs it and activates it, and until then the package must read as unavailable — this
    /// is the same "import ≠ available" gate the activation action enforces.
    /// </summary>
    [Fact]
    public async Task GetPublicPackages_ShouldNotCountUnactivatedImportedStockAsAvailable()
    {
        _context.FuelNominals.Add(CreatePackage("pkg-1", "Brand-a", "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher("Brand-a", "ft-a", 10m, status: VoucherStatus.Imported));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle().Which.IsAvailable.Should().BeFalse();
    }

    /// <summary>
    /// The QA⇄test split is a fulfilment guarantee (#914): a test voucher can never reach a paying
    /// customer. The catalog has to honour it too, or it advertises to a real customer stock that
    /// fulfilment would then refuse to hand over.
    /// </summary>
    [Fact]
    public async Task GetPublicPackages_ShouldNotShowTestStockToARegularCustomer()
    {
        _context.FuelNominals.Add(CreatePackage("pkg-1", "Brand-a", "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher("Brand-a", "ft-a", 10m, isTestData: true));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle().Which.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicPackages_ShouldMatchStockByBrandCaseInsensitively()
    {
        // The catalog carries "Brand-a"; imported stock carries whatever the supplier printed.
        // Fulfilment compares case-insensitively, so a case difference must not read as "no stock".
        _context.FuelNominals.Add(CreatePackage("pkg-1", "Brand-a", "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher("Brand-A", "ft-a", 10m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle().Which.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicPackages_ShouldIgnoreExpiredStock_WhileExpiryIsEnforced()
    {
        _context.FuelNominals.Add(CreatePackage("pkg-1", "Brand-a", "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher(
            "Brand-a", "ft-a", 10m, providerExpiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle().Which.IsAvailable.Should().BeFalse();
    }

    /// <summary>
    /// The sandbox escape hatch has to move both sides together. While <c>VoucherExpiration:Enabled</c>
    /// is off the worker sells expired stock anyway, so the shop must not grey it out — otherwise the
    /// catalogue and the worker disagree during a sandbox run.
    /// </summary>
    [Fact]
    public async Task GetPublicPackages_ShouldCountExpiredStock_WhileExpiryIsDisabled()
    {
        _context.FuelNominals.Add(CreatePackage("pkg-1", "Brand-a", "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher(
            "Brand-a", "ft-a", 10m, providerExpiration: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock(
            new Dictionary<string, string?> { ["VoucherExpiration:Enabled"] = "false" }));

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle().Which.IsAvailable.Should().BeTrue();
    }

    /// <summary>
    /// The second anonymous storefront route carries the same flag; a filter on the first alone would
    /// let it advertise stock checkout then refuses.
    /// </summary>
    [Fact]
    public async Task GetPublicPackagesByBrand_ShouldFlagAvailabilityToo()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-in-stock", "Brand-a", "ft-a", "A-95", 10m),
            CreatePackage("pkg-empty", "Brand-a", "ft-a", "A-95", 20m));
        _context.FuelVouchers.Add(CreateVoucher("Brand-a", "ft-a", 10m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesByBrandQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesByBrandQuery("Brand-a", CallerUserId: null));

result.Should().HaveCount(2);
    var inStock = PackageIds.For(OfferKeys.For("ft-a", DataKinds.Real), 10m);
    var empty = PackageIds.For(OfferKeys.For("ft-a", DataKinds.Real), 20m);
    result.Single(p => p.Id == inStock).IsAvailable.Should().BeTrue();
    result.Single(p => p.Id == empty).IsAvailable.Should().BeFalse();
}

    /// <summary>
    /// The literal reason the shop may not advertise it is operational, not commercial: the supplier
    /// cost is the operator's crown jewel and this DTO goes to anonymous callers.
    /// </summary>
    [Fact]
    public void PublicPackageResponse_ShouldNotExposeSupplierCostOrMargin()
    {
        // Structural guard: the public DTO must not carry cost/margin at all, so no future projection
        // change or entity swap can leak the operator's crown-jewel pricing to anonymous callers.
        var publicFields = typeof(PublicPackageResponse).GetProperties().Select(p => p.Name).ToArray();

        // Named as strings rather than through <c>nameof(FuelPackage)</c>: the package DTO is gone
        // with the volume it projected from, and this guard is about what the public shape must NOT
        // carry - which is a statement about names, not about a type that no longer exists.
        publicFields.Should().NotContain("SupplierPricePerLiter");
        publicFields.Should().NotContain("MarginUahPerLiter");
        publicFields.Should().NotContain("MarginPercent");
    }

    [Fact]
    public async Task GetAdminFuelTypes_ShouldReturnAllFuelTypes()
    {
        _context.FuelTypes.AddRange(
            CreateFuelType("ft-a", "A-95", "Brand-a"),
            CreateFuelType("ft-b", "Diesel", "Brand-b"));
        await _context.SaveChangesAsync();

        var handler = new GetAdminFuelTypesQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminFuelTypesQuery());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAdminFuelTypeById_ShouldReturnFound()
    {
        _context.FuelTypes.Add(CreateFuelType("ft-a", "A-95", "Brand-a"));
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
    public async Task GetAdminBrandById_ShouldReturnFound()
    {
        _context.Brands.Add(CreateBrand("Brand-a", "Alpha Brand"));
        await _context.SaveChangesAsync();

        var handler = new GetAdminBrandByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminBrandByIdQuery("Brand-a"));

        result.Should().NotBeNull();
        result!.Name.Should().Be("Alpha Brand");
    }

    [Fact]
    public async Task GetAdminBrandById_ShouldReturnNull_WhenMissing()
    {
        var handler = new GetAdminBrandByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminBrandByIdQuery("missing"));

        result.Should().BeNull();
    }

    /// <summary>
    /// The stock rule reads <c>VoucherExpiration:Enabled</c> from configuration, exactly as the
    /// fulfilment worker does. Defaults to on, so a test that does not care about expiry gets the
    /// strict, production behaviour unless it says otherwise.
    /// </summary>
    private PackageStockAvailability Stock(Dictionary<string, string?>? settings = null)
        => new(_context, new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build());

    private static Brand CreateBrand(string id, string name)
    {
        return new Brand
        {
            Id = id,
            Name = name,
            LogoText = "LOGO"
        };
    }

    private static BrandStation CreateBrandStation(string id, string BrandId, string name)
    {
        return new BrandStation
        {
            Id = id,
            BrandId = BrandId,
            Name = name
        };
    }

    private static FuelTypeEntity CreateFuelType(string id, string name, string BrandId)
    {
        return new FuelTypeEntity
        {
            Id = id,
            Name = name,
            BrandId = BrandId,
            BasePrice = 50,
            DiscountPrice = 45
        };
    }

    /// <summary>
    /// Builds one volume and seeds the offer behind it.
    /// </summary>
    /// <remarks>
    /// Returns the volume rather than adding it, so every <c>AddRange(CreatePackage(...), ...)</c> in
    /// this file keeps working with only the DbSet renamed. The offer is added here because EF will
    /// cascade the volumes in, but will not invent the parent they point at.
    /// </remarks>
    private FuelNominal CreatePackage(string id, string BrandId, string fuelTypeId, string fuelName, decimal liters, bool isTestData = false)
    {
        // The storefront joins volumes -> offers -> fuel_types for the display name. On the in-memory
        // provider that join is executed for real, so a fixture that seeds a package without seeding its
        // fuel now returns an EMPTY catalogue rather than one with a blank label - and the test fails
        // as "found 0" with nothing pointing at the cause. Seed the fuel alongside the package.
        if (!_context.FuelTypes.Local.Any(f => f.Id == fuelTypeId) && !_context.FuelTypes.Any(f => f.Id == fuelTypeId))
        {
            _context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = fuelTypeId,
                Name = fuelName,
                BrandId = BrandId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }

        // Priced like every other package on the shelf: there is no per-package override any more, so a
    // fixture that leaves the knobs empty seeds a package the public query rightly skips as
    // unpriced. Tests about availability must not depend on that.
    _context.SeedOffer(
        BrandId, fuelTypeId, isTestData, supplierPricePerLiter: 49m, marginUahPerLiter: 7m);

    // The size itself. A nominal is keyed by fuel and litres and belongs to no pool, so this is
    // the whole of what a package is now - there is no second row keeping the two in step.
    var nominal = new FuelNominal
    {
        FuelTypeId = fuelTypeId,
        Liters = liters,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };
    _context.FuelNominals.Add(nominal);
    return nominal;
    }

    private static FuelVoucher CreateVoucher(
        string brand,
        string fuelTypeId,
        decimal liters,
        VoucherStatus status = VoucherStatus.Available,
        bool isTestData = false,
        DateOnly? providerExpiration = null)
    {
        var expiry = providerExpiration ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));
        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Brand = brand,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"{brand}-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            IsTestData = isTestData,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }
}
