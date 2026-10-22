using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.AdminAddFuel;
using FuelFlow.Features.Brands.AdminDeleteFuel;
using FuelFlow.Features.Brands.AdminUpdateFuel;
using FuelFlow.Features.Brands.AdminUpdateNominals;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

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
        // The controller asks a handler for the sizes; the handler owns the database and the audit
        // trail. They all share this one context, exactly as the controller used to.
        var events = new AuditEventService(_context);
        var prices = new PriceHistoryRecorder(_context);
        var controller = new BrandFuelController(
            new GetFuelPriceHistoryQueryHandler(_context),
            new GetFuelPriceAtQueryHandler(_context),
            new AdminAddFuelCommandHandler(_context, events, prices),
            new AdminUpdateFuelCommandHandler(_context, events, prices),
            new AdminDeleteFuelCommandHandler(_context, events),
            new AdminUpdateNominalsCommandHandler(_context, events));

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

        await CreateController().UpdateNominals(BrandIds.Okko, new List<int> { 10, 20, 30 }, default);

        // The size is now stated once, on the fuel. Everything else - the volumes per pool, the shelf -
        // follows from this list rather than being its own copy of the answer.
        var sizes = await _context.FuelNominals.AsNoTracking()
            .Where(n => n.FuelTypeId == ToUuid("okko-a95"))
            .OrderBy(n => n.Liters)
            .Select(n => n.Liters)
            .ToListAsync();

        sizes.Should().Equal(10m, 20m, 30m);
    }

    [Fact]
    public async Task A_Nominal_The_Operator_Removes_Leaves_The_Fuels_Nominal_List()
    {
        await SeedAsync(10, 20);

        await CreateController().UpdateNominals(BrandIds.Okko, new List<int> { 10 }, default);

        var sizes = await _context.FuelNominals.AsNoTracking()
            .Where(n => n.FuelTypeId == ToUuid("okko-a95"))
            .Select(n => n.Liters)
            .ToListAsync();

        sizes.Should().Equal(10m);
    }
    private async Task<List<FuelNominal>> SeedAsync(params int[] nominals)
    {
        _context.Brands.Add(new Brand
        {
            Id = BrandIds.Okko,
            Slug = "okko",
            Name = "okko",
            Color = "#00ff80",
            LogoText = "OK",
            SortOrder = 1,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("okko-a95"),
            Name = "А95",
            BrandId = BrandIds.Okko,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });

// Seeded as one offer plus a volume per nominal, and returned so the tests can assert on volume ids -
        // that is what the nominals edit now reuses instead of the package rows. The per-litre figures
        // go on the offer: the edit restates each volume's total from them.
        _context.SeedOffer(BrandIds.Okko, "okko-a95",
            supplierPricePerLiter: 90m, marginUahPerLiter: 10m, finalPricePerLiter: 100m);
        var rows = nominals.Select(l => new FuelNominal
        {
            FuelTypeId = ToUuid("okko-a95"),
            Liters = l,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        }).ToList();

        _context.FuelNominals.AddRange(rows);
        await _context.SaveChangesAsync();
        return rows;
    }

    [Fact]
    public async Task A_Nominal_That_Leaves_Is_Removed_Not_Archived()
    {
        await SeedAsync(10, 20);

        await CreateController().UpdateNominals(BrandIds.Okko, new List<int> { 10 }, default);

        var sizes = await _context.FuelNominals.AsNoTracking()
            .Where(n => n.FuelTypeId == ToUuid("okko-a95"))
            .Select(n => n.Liters)
            .ToListAsync();

        // Straight removal, not the archive #217 insisted on, and the reason is that a nominal is no
        // longer a row anything points at: `order_line_items` carries brand, fuel, litres and the
        // prices paid, and no table in the schema holds a nominal or package id. Archiving existed to
        // keep the row an order referenced resolvable; there is no such row. What an order line still
        // has to survive - deleting a FUEL that vouchers were sold against - is untouched by this.
        sizes.Should().Equal(10m);
    }

    [Fact]
    public async Task Repeating_The_Same_Nominals_Twice_Changes_Nothing()
    {
        await SeedAsync(10, 20);

        var controller = CreateController();
        await controller.UpdateNominals(BrandIds.Okko, new List<int> { 10, 20 }, default);
        var afterFirst = await _context.FuelNominals.AsNoTracking()
            .Select(n => new { n.FuelTypeId, n.Liters }).ToListAsync();

        await controller.UpdateNominals(BrandIds.Okko, new List<int> { 10, 20 }, default);
        var afterSecond = await _context.FuelNominals.AsNoTracking()
            .Select(n => new { n.FuelTypeId, n.Liters }).ToListAsync();

        // Under the old code this grew by two rows on every save - archived duplicates piling up on
        // each price edit.
        afterSecond.Should().BeEquivalentTo(afterFirst, "re-applying the same nominal set is a no-op");
        afterSecond.Should().HaveCount(2);
    }

    /// <summary>
    /// The endpoint takes the size list straight off the wire and writes it, so a non-positive value
    /// has to be rejected before anything is touched: a bare 23514 from the database names neither the
    /// offending value nor this endpoint, and a single bad entry in the list must not take the
    /// surviving sizes down on its way out.
    /// </summary>
    [Theory]
    [InlineData(new[] { 10, 0 })]
    [InlineData(new[] { 10, -10 })]
    [InlineData(new[] { -5 })]
    public async Task A_NonPositive_Nominal_Is_Rejected_Without_Touching_The_Catalogue(int[] nominals)
    {
        await SeedAsync(10, 20);
        var before = await _context.FuelNominals.AsNoTracking().CountAsync();

        var result = await CreateController().UpdateNominals(BrandIds.Okko, nominals.ToList(), default);

        var bad = result as BadRequestObjectResult;
        bad.Should().NotBeNull("a negative volume is not a product");
        bad!.Value.Should().NotBeNull();

        (await _context.FuelNominals.AsNoTracking().CountAsync()).Should().Be(before,
            "a rejected request must not archive anything");
    }
}
