using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Providers;

/// <summary>
/// <c>UpdateNominals</c> used to RemoveRange the Brand's packages and recreate the whole set with
/// fresh ids. That destroyed packages an order_line_item points at, and meant the package a customer
/// bought was never the package in the catalog (#217).
/// </summary>
public sealed class UpdateNominalsReuseTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public UpdateNominalsReuseTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"nominals-{Guid.NewGuid():N}")
            .Options;
        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public void Dispose() => _context.Dispose();

    private BrandFuelController CreateController()
    {
        var controller = new BrandFuelController(
            new GetFuelPriceHistoryQueryHandler(_context),
            new GetFuelPriceAtQueryHandler(_context),
            new AuditEventService(_context),
            _context,
            NotificationDispatcher.Disabled,
            new OfferPriceRecorder(_context));

        // UpdateNominals records an audit event attributed to the caller, so the controller needs a
        // principal - there is no ASP.NET pipeline in a unit test.
        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new System.Security.Claims.Claim("first_name", "Test"),
                    new System.Security.Claims.Claim("last_name", "Operator"),
                ],
                "TestAuth"));

        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = claims },
        };

        return controller;
    }


    [Fact]
    public async Task A_Nominal_The_Operator_Adds_Is_Written_To_The_Fuels_Nominal_List()
    {
        await SeedAsync(10, 20);

        await CreateController().UpdateNominals("okko", new List<int> { 10, 20, 30 }, default);

        // The size is now stated once, on the fuel. Everything else - the volumes per pool, the shelf -
        // follows from this list rather than being its own copy of the answer.
        var sizes = await _context.FuelNominals.AsNoTracking()
            .Where(n => n.FuelTypeId == "okko-a95")
            .OrderBy(n => n.Liters)
            .Select(n => n.Liters)
            .ToListAsync();

        sizes.Should().Equal(10m, 20m, 30m);
    }

    [Fact]
    public async Task A_Nominal_The_Operator_Removes_Leaves_The_Fuels_Nominal_List()
    {
        await SeedAsync(10, 20);

        await CreateController().UpdateNominals("okko", new List<int> { 10 }, default);

        var sizes = await _context.FuelNominals.AsNoTracking()
            .Where(n => n.FuelTypeId == "okko-a95")
            .Select(n => n.Liters)
            .ToListAsync();

        sizes.Should().Equal(10m);
    }
    private async Task<List<FuelOfferVolume>> SeedAsync(params int[] nominals)
    {
        _context.Brands.Add(new Brand
        {
            Id = "okko",
            Name = "OKKO",
            Color = "#00ff80",
            LogoText = "OK",
            SortOrder = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-a95",
            Name = "А95",
            BrandId = "okko",
            BasePrice = 50m,
            DiscountPrice = 45m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

// Seeded as one offer plus a volume per nominal, and returned so the tests can assert on volume ids -
        // that is what the nominals edit now reuses instead of the package rows. The per-litre figures
        // go on the offer: the edit restates each volume's total from them.
        var volumes = nominals.Select(l => new FuelOfferVolume
        {
            Id = Guid.NewGuid().ToString(),
            OfferId = _context.SeedOffer(
                "okko", "okko-a95",
                supplierPricePerLiter: 90m, marginUahPerLiter: 10m, finalPricePerLiter: 100m).Id,
            Liters = l,
            PriceOverrideUah = 1000m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        }).ToList();

        _context.FuelOfferVolumes.AddRange(volumes);
        await _context.SaveChangesAsync();
        return volumes;
    }

    [Fact]
    public async Task A_Nominal_That_Stays_Keeps_Its_Id_And_Its_CreatedAt()
    {
        var seeded = await SeedAsync(10, 20);
        var tenL = seeded.Single(p => p.Liters == 10m);
        var createdAt = tenL.CreatedAtUtc;

        await CreateController().UpdateNominals("okko", new List<int> { 10, 20, 30 }, default);

        // The 10 L package must be the SAME row. If it were removed and recreated the id would
        // change, and every order_line_item pointing at it would stop resolving.
        var after = await _context.FuelOfferVolumes.IgnoreQueryFilters().SingleAsync(p => p.Id == tenL.Id);
        after.Liters.Should().Be(10m);
        after.CreatedAtUtc.Should().Be(createdAt, "a package that stayed is the same package");
    }

    [Fact]
    public async Task A_Nominal_That_Leaves_Is_Archived_Not_Removed()
    {
        var seeded = await SeedAsync(10, 20);
        var twentyL = seeded.Single(p => p.Liters == 20m);

        await CreateController().UpdateNominals("okko", new List<int> { 10 }, default);

        var archived = await _context.FuelOfferVolumes.IgnoreQueryFilters().SingleAsync(p => p.Id == twentyL.Id);
        archived.IsDeleted.Should().BeTrue("#217: the row an order may reference must survive");
        archived.DeletedAtUtc.Should().NotBeNull();

        (await _context.FuelOfferVolumes.CountAsync()).Should().Be(1,
            "the global filter hides the archived one, so the catalog shows just the 10 L");
    }

    [Fact]
    public async Task Repeating_The_Same_Nominals_Twice_Changes_Nothing()
    {
        await SeedAsync(10, 20);

        var controller = CreateController();
        await controller.UpdateNominals("okko", new List<int> { 10, 20 }, default);
        var afterFirst = await _context.FuelOfferVolumes.IgnoreQueryFilters()
            .Select(p => new { p.Id, p.Liters }).ToListAsync();

        await controller.UpdateNominals("okko", new List<int> { 10, 20 }, default);
        var afterSecond = await _context.FuelOfferVolumes.IgnoreQueryFilters()
            .Select(p => new { p.Id, p.Liters }).ToListAsync();

        // Under the old code this grew by two rows on every save - archived duplicates piling up on
        // each price edit.
        afterSecond.Should().BeEquivalentTo(afterFirst, "re-applying the same nominal set is a no-op");
        afterSecond.Should().HaveCount(2);
    }

    /// <summary>
    /// The body binds <c>List&lt;int&gt;</c> straight off the wire, so a nominal of <c>-10</c> used
    /// to create a package priced at <c>-10 ×</c> the per-litre rate and the storefront listed it.
    /// <c>ck_fuel_packages_liters_positive</c> now stops it at the database, but a bare 23514 names
    /// neither the offending value nor this endpoint - so the rejection happens here, where the
    /// answer can say which nominal and why.
    /// </summary>
    [Theory]
    [InlineData(new[] { 10, 0 })]
    [InlineData(new[] { 10, -10 })]
    [InlineData(new[] { -5 })]
    public async Task A_NonPositive_Nominal_Is_Rejected_Without_Touching_The_Catalogue(int[] nominals)
    {
        await SeedAsync(10, 20);
        var before = await _context.FuelOfferVolumes.IgnoreQueryFilters().CountAsync();

        var result = await CreateController().UpdateNominals("okko", nominals.ToList(), default);

        var bad = result as BadRequestObjectResult;
        bad.Should().NotBeNull("a negative volume is not a product");
        bad!.Value.Should().NotBeNull();

        // The strongest part: the rejection happens before any of the archive-then-add logic runs,
        // so a single bad value in the list cannot archive the surviving volumes on its way out.
        (await _context.FuelOfferVolumes.IgnoreQueryFilters().CountAsync()).Should().Be(before,
            "a rejected request must not archive anything");
    }
}
