using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Providers;

/// <summary>
/// <c>is_test_data</c> decides whether a package is sellable, and it used to be optional: a plain
/// <c>bool</c> on the request DTOs, so a client that omitted it got <c>false</c> — which means "put it
/// on sale".
/// <para>
/// Production absorbed two rows that way: <c>QA-CHECK-95</c> at 1 UAH, and
/// <c>А95 ЄВРО (ТЕСТ 1ГРН)</c> at 1 UAH as <c>test-1uah-okko-a95eu</c>, the second served to real
/// customers by the same anonymous endpoint the marketing site reads. Both were rehearsal data
/// nobody meant to publish.
/// </para>
/// <para>
/// The second is the worse shape: <c>PUT /brands/fuels/{id}</c> rewrites the flag on <em>every</em>
/// package of the fuel on every save, so editing a QA fuel's margin from a client that omitted the
/// field published the whole fuel, silently.
/// </para>
/// </summary>
public sealed class RehearsalFlagWritePathTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly BrandsController _controller;

    public RehearsalFlagWritePathTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _controller = new BrandsController(
            new GetBrandsQueryHandler(_context),
            new GetBrandByIdQueryHandler(_context),
            new GetBrandHistoryQueryHandler(_context),
            new AuditEventService(_context),
            _context,
            NotificationDispatcher.Disabled);

        // The success path records an audit event attributed to the caller, so the controller needs
        // a principal - there is no ASP.NET pipeline in a unit test.
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                            new Claim("first_name", "Test"),
                            new Claim("last_name", "Operator"),
                        ],
                        "TestAuth"))
            }
        };

        _context.Stations.Add(new Station
        {
            Id = "okko", Name = "OKKO", Color = "#22c55e", LogoText = "OKKO",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task UpdateFuel_WithoutTheFlag_IsRefused_RatherThanPublished()
    {
        await SeedQaFuelAsync();

        // The exact request a client sends when it does not know about the field.
        var result = await _controller.UpdateFuel(
            "qa-a95",
            new BrandFuelDto
            {
                Id = "qa-a95",
                Name = "А95 ЄВРО",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                AllowBelowCost = false,
                // IsTestData deliberately absent.
                IsTestData = null
            },
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);

        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == "qa-a95").ToListAsync();
        packages.Should().OnlyContain(p => p.IsTestData,
            "an edit that does not say must not be able to put rehearsal stock on sale");
    }

    [Fact]
    public async Task AddFuel_WithoutTheFlag_IsRefused()
    {
        var result = await _controller.AddFuel(
            "okko",
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = null
            },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// The second half of the fix, and the one that reads as overreach until it bites: a name that
    /// says rehearsal cannot be published as live even when the flag is explicitly false.
    /// <para>
    /// The flag stays the real control — an operator may innocently name a grade "Test-A95" and mean
    /// a product — but the silent combination (name says rehearsal, flag says sell) is exactly what
    /// reached the public storefront, and nothing else in the system objects to it.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("А95 ЄВРО (ТЕСТ 1ГРН)", null)]
    [InlineData("QA-CHECK-95", null)]
    [InlineData("ДП PULLS(QA)", null)]
    [InlineData("test-1uah-okko-a95eu", null)]
    public async Task ARehearsalLookingFuel_CannotBeSavedAsLive(string name, bool? _)
    {
        var result = await _controller.AddFuel(
            "okko",
            new CreateFuelRequest
            {
                Name = name,
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = false
            },
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task ARehearsalLookingFuel_IsAccepted_WhenTheFlagSaysRehearsal()
    {
        // The guard must not make rehearsal naming impossible - the seeds name rows exactly like this.
        var result = await _controller.AddFuel(
            "okko",
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО (ТЕСТ 1ГРН)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = true
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>(
            "the flag is the control; the name is only a second opinion");
    }

    [Fact]
    public async Task AnOrdinaryFuel_IsUnaffected()
    {
        var result = await _controller.AddFuel(
            "okko",
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = false
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
    }

    /// <summary>
    /// The false-positive guard. A rehearsal-looking block would block a real product from being
    /// published, which is the same outage in the opposite direction - so every fuel name the
    /// catalogue actually ships with is asserted clean, taken from production on 2026-10-14.
    /// </summary>
    [Theory]
    [InlineData("А95 ЄВРО")]
    [InlineData("ДП ЄВРО")]
    [InlineData("ГАЗ")]
    [InlineData("ДП PULLS")]
    [InlineData("А 92")]
    [InlineData("А 95+")]
    [InlineData("upg95")]
    [InlineData("95 Євро5-Е10")]
    [InlineData("EURO DIESEL")]
    public async Task OrdinaryFuelNames_AreNotMistakenForRehearsal(string name)
    {
        var result = await _controller.AddFuel(
            "okko",
            new CreateFuelRequest
            {
                Name = name,
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = false
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>(
            "a false positive here would block a real product from being published");
    }

    private async Task SeedQaFuelAsync()
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "qa-a95", StationId = "okko", Name = "А95 ЄВРО(QA)",
            BasePrice = 92.90m, DiscountPrice = 72.34m,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelPackages.Add(new FuelPackage
        {
            Id = "qa-pkg-a95", StationId = "okko", FuelTypeId = "qa-a95",
            FuelName = "А95 ЄВРО(QA)", Liters = 10m, Price = 723.40m,
            FinalPricePerLiter = 72.34m, IsTestData = true,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }
}
