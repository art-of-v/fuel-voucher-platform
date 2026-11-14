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
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

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
            CreateBrand(BrandId(2), "Beta Brand", "beta"),
            CreateBrand(BrandId(1), "Alpha Brand", "alpha"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicBrandsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicBrandsQuery());

        result.Should().HaveCount(2);
        result.Select(s => s.Id).Should().Contain(new[] { BrandId(1), BrandId(2) });
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
            _context.Brands.Add(CreateBrand(BrandId(i), $"Brand {i}"));
        }
        await _context.SaveChangesAsync();

        var handler = new GetAdminBrandsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminBrandsQuery(Page: 1, PageSize: 2));

        result.Items.Should().HaveCount(2);
        result.Items.Select(s => s.Id).Should().Equal(BrandId(1), BrandId(2));
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
            _context.Brands.Add(CreateBrand(BrandId(i), $"Brand {i}"));
        }
        await _context.SaveChangesAsync();

        var handler = new GetAdminBrandsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminBrandsQuery(Page: 3, PageSize: 2));

        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(BrandId(5));
        result.HasPreviousPage.Should().BeTrue();
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicBrandStations_ShouldReturnAllNodes()
    {
        _context.BrandStations.AddRange(
CreateBrandStation(StationId(1), BrandId(1), "Node A"),
                CreateBrandStation(StationId(2), BrandId(2), "Node B"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicBrandStationsQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicBrandStationsQuery());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPublicBrandStationsByBrand_ShouldFilterByBrandId()
    {
        _context.BrandStations.AddRange(
            CreateBrandStation(StationId(1), BrandId(1), "Node A1"),
            CreateBrandStation(StationId(2), BrandId(1), "Node A2"),
            CreateBrandStation(StationId(3), BrandId(2), "Node B"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicBrandStationsByBrandQueryHandler(_context);

        var result = await handler.HandleAsync(new GetPublicBrandStationsByBrandQuery(BrandId(1)));

        result.Should().HaveCount(2);
    }

    /// <summary>
    /// A fuel the catalogue import has just created is shown, priced at the column price, and marked
    /// unavailable.
    /// </summary>
    /// <remarks>
    /// The import creates a fuel with the published pump price and no cost, because our cost is not
    /// something it knows. It used to be SKIPPED by the storefront - "a zero on the shelf is a free
    /// fuel" - which made the shelf empty, and an empty shelf says "we do not sell this" rather than
    /// "we have not costed this yet". The stock badge is the honest signal: there is nothing to buy
    /// until a batch of vouchers is costed, and the checkout refuses it with a 400.
    /// </remarks>
    [Fact]
    public async Task GetPublicPackages_ShouldShowAFuelWeHaveNotCosted_AtTheColumnPrice_AndUnavailable()
    {
        EnsureBrand(BrandId(1));

        // Exactly what the import writes: a pump price and no cost, no margin.
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("ft-imported"),
            Name = "А-95",
            BrandId = BrandId(1),
            PumpPricePerLiter = 92.90m,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });
        _context.FuelNominals.Add(new FuelNominal
        {
            FuelTypeId = ToUuid("ft-imported"),
            Liters = 10m,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        var package = result.Should().ContainSingle().Subject;
        package.Price.Should().Be(929.00m, "the column price is the price we can show until we have a cost");
        package.FinalPricePerLiter.Should().Be(92.90m);
        package.IsAvailable.Should().BeFalse("there is no stock behind it, and that is what the badge says");
    }


    [Fact]
    public async Task GetPublicPackages_ShouldReturnAllPackages()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-1", BrandId(1), "ft-a", "A-95", 10m),
            CreatePackage("pkg-2", BrandId(2), "ft-b", "Diesel", 20m));
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
            CreatePackage("pkg-real", BrandId(1), "ft-a", "A-95", 10m),
            CreatePackage("pkg-qa", BrandId(1), "ft-qa", "QA-CHECK-95", 5m, isTestData: true));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle("a rehearsal package reaching the store front is purchasable");
        result[0].Id.Should().Be(PackageIds.For(ToUuid("ft-a"), DataKinds.Real, 10m));
    }

    /// <summary>
    /// The second anonymous route. A filter on GetPublicPackages alone would have left the
    /// rehearsal package buyable through /api/packages/Brand/{id}.
    /// </summary>
    [Fact]
    public async Task GetPublicPackagesByBrand_ShouldNotServeAFlaggedTestPackage()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-real", BrandId(1), "ft-a", "A-95", 10m),
            CreatePackage("pkg-qa", BrandId(1), "ft-qa", "QA-CHECK-95", 5m, isTestData: true));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesByBrandQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesByBrandQuery(BrandId(1), CallerUserId: null));

        result.Should().ContainSingle();
        result[0].Id.Should().Be(PackageIds.For(ToUuid("ft-a"), DataKinds.Real, 10m));
    }

    [Fact]
    public async Task GetPublicPackagesByBrand_ShouldFilterByBrandId()
    {
        _context.FuelNominals.AddRange(
            CreatePackage("pkg-1", BrandId(1), "ft-a", "A-95", 10m),
            CreatePackage("pkg-2", BrandId(1), "ft-a", "A-95", 20m),
            CreatePackage("pkg-3", BrandId(2), "ft-b", "Diesel", 20m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesByBrandQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesByBrandQuery(BrandId(1), CallerUserId: null));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.BrandId == BrandId(1));
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
        _context.FuelNominals.Add(CreatePackage("pkg-1", BrandId(1), "ft-a", "A-95", 10m));
        _context.SeedOffer(BrandId(1), "ft-a",
            pumpPricePerLiter: 60m,      // public list price -> fresh "before" = 600
            finalPricePerLiter: 50m,
            supplierPricePerLiter: 42m,
            marginUahPerLiter: 8m,
            marginPercent: 19m);
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        var pkg = result.Should().ContainSingle().Which;
// The id is derived from the fuel, the pool and the size, because a package is not a row
    // any more. Asserted through PackageIds rather than spelled out, so a change to the format
    // fails here instead of quietly splitting the shelf from the checkout resolver.
    pkg.Id.Should().Be(PackageIds.For(ToUuid("ft-a"), DataKinds.Real, 10m));
    pkg.BrandId.Should().Be(BrandId(1));
        pkg.FuelTypeId.Should().Be(ToUuid("ft-a"));
        pkg.FuelName.Should().Be("A-95");
        pkg.Liters.Should().Be(10m);
        pkg.Price.Should().Be(600);
        // Pump × liters (60 × 10), NOT the stale stored 420 nor the supplier cost.
        pkg.OriginalPrice.Should().Be(600);
        // Out of stock, the shelf shows the COLUMN price - our price is what we charge when the
        // fuel is there. The saving is shown only when there is stock to save it on.
        pkg.FinalPricePerLiter.Should().Be(60m);
    }

    [Fact]
    public async Task GetPublicPackages_NullPump_FallsBackToSalePrice_SoNoFabricatedSaving()
    {
        // Legacy fuel priced before any column price was recorded: with no column price the "before"
        // price is just the sale price, so the store-front shows no fabricated saving — and still
        // never the stored cost-derived OriginalPrice (planning #73).
// The legacy case: a package with no pump price at all. There is nothing to recompute the struck
        // price from, so the storefront falls back to the sale price and shows no fabricated saving.
        _context.FuelNominals.Add(CreatePackage("pkg-legacy", BrandId(1), "ft-a", "A-95", 10m));
        _context.SeedOffer(BrandId(1), "ft-a",
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
            CreatePackage("pkg-in-stock", BrandId(1), "ft-a", "A-95", 10m),
            CreatePackage("pkg-empty", BrandId(1), "ft-a", "A-95", 20m));
        _context.FuelVouchers.Add(CreateVoucher(BrandId(1), "ft-a", 10m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

result.Should().HaveCount(2, "an empty shelf is greyed out, not removed from the catalog");
    var inStock = PackageIds.For(ToUuid("ft-a"), DataKinds.Real, 10m);
    var empty = PackageIds.For(ToUuid("ft-a"), DataKinds.Real, 20m);
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
        _context.FuelNominals.Add(CreatePackage("pkg-1", BrandId(1), "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher(BrandId(1), "ft-a", 10m, status: VoucherStatus.Imported));
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
        _context.FuelNominals.Add(CreatePackage("pkg-1", BrandId(1), "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher(BrandId(1), "ft-a", 10m, isTestData: true));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle().Which.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetPublicPackages_ShouldMatchStockByBrandCaseInsensitively()
    {
        // The catalogue carries the network's code in its own case; imported stock carries whatever
        // the supplier printed. Fulfilment compares case-insensitively, so a case difference must not
        // read as "no stock".
        _context.FuelNominals.Add(CreatePackage("pkg-1", BrandId(1), "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher(BrandId(1), "ft-a", 10m, brandText: "ALPHA"));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesQuery(CallerUserId: null));

        result.Should().ContainSingle().Which.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetPublicPackages_ShouldIgnoreExpiredStock_WhileExpiryIsEnforced()
    {
        _context.FuelNominals.Add(CreatePackage("pkg-1", BrandId(1), "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher(
            BrandId(1), "ft-a", 10m, providerExpiration: DateOnly.FromDateTime(TestClock.UtcNow.AddDays(-1))));
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
        _context.FuelNominals.Add(CreatePackage("pkg-1", BrandId(1), "ft-a", "A-95", 10m));
        _context.FuelVouchers.Add(CreateVoucher(
            BrandId(1), "ft-a", 10m, providerExpiration: DateOnly.FromDateTime(TestClock.UtcNow.AddDays(-1))));
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
            CreatePackage("pkg-in-stock", BrandId(1), "ft-a", "A-95", 10m),
            CreatePackage("pkg-empty", BrandId(1), "ft-a", "A-95", 20m));
        _context.FuelVouchers.Add(CreateVoucher(BrandId(1), "ft-a", 10m));
        await _context.SaveChangesAsync();

        var handler = new GetPublicPackagesByBrandQueryHandler(_context, Stock());

        var result = await handler.HandleAsync(new GetPublicPackagesByBrandQuery(BrandId(1), CallerUserId: null));

result.Should().HaveCount(2);
    var inStock = PackageIds.For(ToUuid("ft-a"), DataKinds.Real, 10m);
    var empty = PackageIds.For(ToUuid("ft-a"), DataKinds.Real, 20m);
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
            CreateFuelType("ft-a", "A-95", BrandId(1)),
            CreateFuelType("ft-b", "Diesel", BrandId(2)));
        await _context.SaveChangesAsync();

        var handler = new GetAdminFuelTypesQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminFuelTypesQuery());

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAdminFuelTypeById_ShouldReturnFound()
    {
        _context.FuelTypes.Add(CreateFuelType("ft-a", "A-95", BrandId(1)));
        await _context.SaveChangesAsync();

        var handler = new GetAdminFuelTypeByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminFuelTypeByIdQuery(ToUuid("ft-a")));

        result.Should().NotBeNull();
        result!.Name.Should().Be("A-95");
    }

    [Fact]
    public async Task GetAdminFuelTypeById_ShouldReturnNull_WhenMissing()
    {
        var handler = new GetAdminFuelTypeByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminFuelTypeByIdQuery(ToUuid("missing")));

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminBrandById_ShouldReturnFound()
    {
        _context.Brands.Add(CreateBrand(BrandId(1), "Alpha Brand", "alpha"));
        await _context.SaveChangesAsync();

        var handler = new GetAdminBrandByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminBrandByIdQuery(BrandId(1)));

        result.Should().NotBeNull();
        result!.Name.Should().Be("Alpha Brand");
    }

    [Fact]
    public async Task GetAdminBrandById_ShouldReturnNull_WhenMissing()
    {
        var handler = new GetAdminBrandByIdQueryHandler(_context);

        var result = await handler.HandleAsync(new GetAdminBrandByIdQuery(BrandIds.NotFound));

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

    private static Brand CreateBrand(Guid id, string name, string? slug = null)
    {
        return new Brand
        {
            Id = id,
            Name = name,
            // Unique per row: a test that seeds five brands cannot have them share a network code,
            // and a slug derived from the name alone collides the moment two fixtures share one.
            Slug = slug ?? $"brand-{id:N}"[..12],
            LogoText = "LOGO"
        };
    }

    /// <summary>
    /// A stable id for the <paramref name="index"/>-th fixture brand, so a paging test can assert on
    /// which rows came back without the ids changing between runs.
    /// </summary>
    private static Guid BrandId(int index)
        => new($"0b0b0b0b-0000-4000-8000-{index:D12}");

    /// <summary>
    /// The brand row behind <paramref name="brandId"/>, created if this fixture has not made one.
    /// </summary>
    /// <remarks>
    /// Every read path joins to <c>brands</c> for the slug - the stock key is matched on it, and the
    /// DTO carries it beside the uuid - so a station or an offer pointed at a brand nobody inserted
    /// answers nothing. The failure then reads "found 0", which points at the filter rather than at
    /// the row that is missing. The brand used to <em>be</em> its own name and needed no row, which
    /// is why so many fixtures here never made one.
    /// </remarks>
    private Brand EnsureBrand(Guid brandId, string? name = null)
    {
        var existing = _context.Brands.Local.FirstOrDefault(b => b.Id == brandId)
            ?? _context.Brands.FirstOrDefault(b => b.Id == brandId);
        if (existing is not null)
        {
            return existing;
        }

        var slug = SlugOf(brandId);
        var brand = CreateBrand(brandId, name ?? char.ToUpperInvariant(slug[0]) + slug[1..], slug);
        _context.Brands.Add(brand);
        return brand;
    }

    /// <summary>
    /// The network's own code for a fixture brand. The two named ones exist so an assertion can read
    /// <c>"alpha"</c> rather than a slice of a guid; anything else falls back to one derived from the
    /// id, which is unique by construction and which no test asserts on by name.
    /// </summary>
    private static string SlugOf(Guid brandId) => brandId switch
    {
        _ when brandId == BrandId(1) => "alpha",
        _ when brandId == BrandId(2) => "beta",
        _ => $"brand-{brandId:N}"[..12],
    };

    /// <summary>
    /// The same idea for the <paramref name="index"/>-th station: an identity the assertion can name,
    /// with the network's own node id carried beside it as the re-import key.
    /// </summary>
    private static Guid StationId(int index)
        => new($"0c0c0c0c-0000-4000-8000-{index:D12}");

    private BrandStation CreateBrandStation(Guid id, Guid BrandId, string name)
    {
        EnsureBrand(BrandId);

        return new BrandStation
        {
            Id = id,
            BrandId = BrandId,
            // The re-import key: the network's own node id, which is what the import writes back.
            ExternalId = $"{BrandId}-{id:N}",
            Name = name
        };
    }

    private FuelTypeEntity CreateFuelType(string id, string name, Guid BrandId)
    {
        EnsureBrand(BrandId);

        return new FuelTypeEntity
        {
            Id = ToUuid(id),
            Name = name,
            BrandId = BrandId
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
    private FuelNominal CreatePackage(string id, Guid BrandId, string fuelTypeId, string fuelName, decimal liters, bool isTestData = false)
    {
        EnsureBrand(BrandId);

        // The storefront joins volumes -> offers -> fuel_types for the display name. On the in-memory
        // provider that join is executed for real, so a fixture that seeds a package without seeding its
        // fuel now returns an EMPTY catalogue rather than one with a blank label - and the test fails
        // as "found 0" with nothing pointing at the cause. Seed the fuel alongside the package.
        if (!_context.FuelTypes.Local.Any(f => f.Id == ToUuid(fuelTypeId)) && !_context.FuelTypes.Any(f => f.Id == ToUuid(fuelTypeId)))
        {
            _context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = ToUuid(fuelTypeId),
                Name = fuelName,
                BrandId = BrandId,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow,
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
FuelTypeId = ToUuid(fuelTypeId),
        DataKind = DataKinds.ForPool(isTestData),
        Liters = liters,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
    };
    _context.FuelNominals.Add(nominal);
    return nominal;
    }

    private FuelVoucher CreateVoucher(
        Guid brandId,
        string fuelTypeId,
        decimal liters,
        VoucherStatus status = VoucherStatus.Available,
        bool isTestData = false,
        DateOnly? providerExpiration = null,
        string? brandText = null)
    {
        var expiry = providerExpiration ?? DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1));
        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            // A voucher names its network as TEXT - the import prints it, the fulfilment worker reads
            // it, and neither knows a uuid. That is why the stock key is slug-based, so the fixture
            // takes the brand row's own code rather than a literal that could drift from it.
BrandId = BrandIds.ForSlug(brandText ?? EnsureBrand(brandId).Slug),
Brand = brandText ?? EnsureBrand(brandId).Slug,
        FuelTypeId = ToUuid(fuelTypeId),
        Liters = liters,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"{brandText ?? EnsureBrand(brandId).Slug}-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            IsTestData = isTestData,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

        // The stock queries read the summary, not the vouchers - the storefront's package list and
        // the term ladder's stock coverage - and the in-memory provider has no trigger to keep it
        // in step. The row is derived from the voucher, so it cannot disagree with it.
        _context.StockSummary.Add(TestStock.SummaryFor(voucher));
        return voucher;
    }
}
