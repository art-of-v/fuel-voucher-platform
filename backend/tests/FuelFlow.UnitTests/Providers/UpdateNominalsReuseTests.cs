using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Providers.GetProviders;
using FuelFlow.Features.Providers.GetProviderById;
using FuelFlow.Features.Providers.GetProviderHistory;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Providers;

/// <summary>
/// <c>UpdateNominals</c> used to RemoveRange the station's packages and recreate the whole set with
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
        _context = new ApplicationDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    private ProvidersController CreateController()
    {
        var controller = new ProvidersController(
            new GetProvidersQueryHandler(_context),
            new GetProviderByIdQueryHandler(_context),
            new GetProviderHistoryQueryHandler(_context),
            new ProviderEventService(_context),
            _context,
            NotificationDispatcher.Disabled);

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

    private async Task<List<FuelPackage>> SeedAsync(params int[] nominals)
    {
        _context.Stations.Add(new Station
        {
            Id = "okko",
            Name = "OKKO",
            StationType = "brand",
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
            StationId = "okko",
            BasePrice = 50m,
            DiscountPrice = 45m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        var packages = nominals.Select(l => new FuelPackage
        {
            Id = Guid.NewGuid().ToString(),
            StationId = "okko",
            FuelTypeId = "okko-a95",
            FuelName = "А95",
            Liters = l,
            Price = 1000m,
            SupplierPricePerLiter = 90m,
            FinalPricePerLiter = 100m,
            MarginUahPerLiter = 10m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        }).ToList();

        _context.FuelPackages.AddRange(packages);
        await _context.SaveChangesAsync();
        return packages;
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
        var after = await _context.FuelPackages.IgnoreQueryFilters().SingleAsync(p => p.Id == tenL.Id);
        after.Liters.Should().Be(10m);
        after.CreatedAtUtc.Should().Be(createdAt, "a package that stayed is the same package");
    }

    [Fact]
    public async Task A_Nominal_That_Leaves_Is_Archived_Not_Removed()
    {
        var seeded = await SeedAsync(10, 20);
        var twentyL = seeded.Single(p => p.Liters == 20m);

        await CreateController().UpdateNominals("okko", new List<int> { 10 }, default);

        var archived = await _context.FuelPackages.IgnoreQueryFilters().SingleAsync(p => p.Id == twentyL.Id);
        archived.IsDeleted.Should().BeTrue("#217: the row an order may reference must survive");
        archived.DeletedAtUtc.Should().NotBeNull();

        (await _context.FuelPackages.CountAsync()).Should().Be(1,
            "the global filter hides the archived one, so the catalog shows just the 10 L");
    }

    [Fact]
    public async Task Repeating_The_Same_Nominals_Twice_Changes_Nothing()
    {
        await SeedAsync(10, 20);

        var controller = CreateController();
        await controller.UpdateNominals("okko", new List<int> { 10, 20 }, default);
        var afterFirst = await _context.FuelPackages.IgnoreQueryFilters()
            .Select(p => new { p.Id, p.Liters }).ToListAsync();

        await controller.UpdateNominals("okko", new List<int> { 10, 20 }, default);
        var afterSecond = await _context.FuelPackages.IgnoreQueryFilters()
            .Select(p => new { p.Id, p.Liters }).ToListAsync();

        // Under the old code this grew by two rows on every save - archived duplicates piling up on
        // each price edit.
        afterSecond.Should().BeEquivalentTo(afterFirst, "re-applying the same nominal set is a no-op");
        afterSecond.Should().HaveCount(2);
    }
}