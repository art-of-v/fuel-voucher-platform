using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.Features.Pricing;
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
/// <c>data_kind</c> decides whether a package is sellable, and it used to be optional: a plain
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
        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _controller = new BrandsController(
            new GetBrandsQueryHandler(_context),
            new GetBrandByIdQueryHandler(_context),
            new GetBrandHistoryQueryHandler(_context),
            new GetFuelPriceHistoryQueryHandler(_context),
            new AuditEventService(_context),
            _context,
            NotificationDispatcher.Disabled,
            new OfferPriceRecorder(_context));

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

          var offers = await _context.FuelOffers.Where(o => o.FuelTypeId == "qa-a95").ToListAsync();
          offers.Should().OnlyContain(o => o.IsTestData,
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

    /// <summary>
    /// The guard a natural fuel id used to give for free. The id is generated now
    /// (<c>Guid.CreateVersion7</c>), so a second 'А95 ЄВРО' at OKKO no longer collides on the primary
    /// key — and it must not be allowed to land as a second row.
    /// <para>
    /// What a twin costs: two offers for one fuel, one fuel listed twice on the storefront at two
    /// prices, and <c>OkkoFuelClassifier</c> resolving a parsed voucher by name with
    /// <c>FirstOrDefault</c> — so the twin that never received the imported pump price is a landing
    /// place for a real voucher. The database refuses the same thing with
    /// <c>ux_fuel_types_station_name</c>; this is the version that can say which fuel already owns the
    /// name, and it is asserted here on an in-memory context that has no index at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AddingAFuelThatAlreadyExistsAtThatStation_IsRefused()
    {
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync(); // the guard reads the row, not the change tracker

        var result = await _controller.AddFuel(
            "okko",
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = true
            },
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;

        (await _context.FuelTypes.CountAsync(f => f.StationId == "okko" && f.Name == "А95 ЄВРО(QA)"))
            .Should().Be(1, "a second row for the same fuel is a second offer the storefront would list twice");

        (await _context.FuelOffers.CountAsync()).Should().Be(1, "and it must not have created one");
        conflict.Value.Should().NotBeNull("the operator is told which fuel to reprice instead");
    }

    [Fact]
    public async Task TheSameFuelNameAtAnotherStation_IsNotADuplicate()
    {
        // The uniqueness is per station, deliberately: two networks both listing 'ГАЗ' is the
        // catalogue working, not a duplicate.
        await SeedQaFuelAsync();

        _context.Stations.Add(new Station
        {
            Id = "wog", Name = "WOG", Color = "#10b981", LogoText = "WOG",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _controller.AddFuel(
            "wog",
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = true
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task AnArchivedFuel_FreesItsNameForAFreshOne()
    {
        // The write path's half of the partial index: what the admin screen archives is not a
        // candidate for "already exists", so a catalogue can be rebuilt under the same names.
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync();

        var archived = await _context.FuelTypes.FirstAsync(f => f.Id == "qa-a95");
        archived.IsDeleted = true;
        archived.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var result = await _controller.AddFuel(
            "okko",
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = true
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task AddingAFuelWhoseCodeIsTaken_IsRefused()
    {
        // The other half of a fuel's identity, refused in the same shape as the name. A code identifies
        // one fuel per network, so two fuels claiming one is what a catalogue import would resolve
        // arbitrarily - and the database's answer (23505) names neither fuel.
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync();

        var existing = await _context.FuelTypes.FirstAsync(f => f.Id == "qa-a95");
        existing.BrandFuelCode = "9018";
        await _context.SaveChangesAsync();

        var result = await _controller.AddFuel(
            "okko",
            new CreateFuelRequest
            {
                Name = "ДП (code guard test)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                IsTestData = true,
                BrandFuelCode = "9018"
            },
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().NotBeNull("the operator is told which fuel already holds the code");

        (await _context.FuelTypes.CountAsync(f => f.BrandFuelCode == "9018"))
            .Should().Be(1, "and no second fuel claims it");
    }

    [Fact]
    public void ACodeIsStoredTrimmed_AndAnEmptyOneMeansNone()
    {
        // "" and null mean opposite things to a unique index: null is excluded from it, "" is a value.
        // One rule, on the entity, for every write path.
        FuelTypeEntity.NormaliseBrandFuelCode("  A-95 ").Should().Be("A-95");
        FuelTypeEntity.NormaliseBrandFuelCode("   ").Should().BeNull();
        FuelTypeEntity.NormaliseBrandFuelCode(null).Should().BeNull();
    }

    [Fact]
    public async Task RepricingAFuel_LeavesItsCodeAlone()
    {
        // An edit is a repricing. A repricing that silently dropped the code would leave a fuel a
        // catalogue import can no longer match to the network's price sheet - which reads as "we sell
        // this" on screen and as one we stopped importing on the site.
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync();

        var existing = await _context.FuelTypes.FirstAsync(f => f.Id == "qa-a95");
        existing.BrandFuelCode = "9018";
        await _context.SaveChangesAsync();

        await _controller.UpdateFuel(
            "qa-a95",
            new BrandFuelDto
            {
                Id = "qa-a95",
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                AllowBelowCost = false,
                IsTestData = true
                // BrandFuelCode deliberately absent.
            },
            CancellationToken.None);

        var reloaded = await _context.FuelTypes.FirstAsync(f => f.Id == "qa-a95");
        reloaded.BrandFuelCode.Should().Be("9018");
    }

    [Fact]
    public async Task AnEmptyCode_ClearsIt()
    {
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync();

        var existing = await _context.FuelTypes.FirstAsync(f => f.Id == "qa-a95");
        existing.BrandFuelCode = "9018";
        await _context.SaveChangesAsync();

        await _controller.UpdateFuel(
            "qa-a95",
            new BrandFuelDto
            {
                Id = "qa-a95",
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                MinDiscountPerLiter = 4m,
                AllowBelowCost = false,
                IsTestData = true,
                BrandFuelCode = ""
            },
            CancellationToken.None);

        var reloaded = await _context.FuelTypes.FirstAsync(f => f.Id == "qa-a95");
        reloaded.BrandFuelCode.Should().BeNull();
    }

    private async Task SeedQaFuelAsync()
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "qa-a95", StationId = "okko", Name = "А95 ЄВРО(QA)",
            BasePrice = 92.90m, DiscountPrice = 72.34m,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        // The rehearsal flag lives on the OFFER now - it is what the storefront filters on, and what
        // `EnforceDataKind` backfilled onto. The volume is here because a fuel with no nominal on it
        // is not a fuel the storefront can sell, and these assertions are about what the admin
        // screens read back.
        var offer = new FuelOffer
        {
            Id = "qa-offer-a95", StationId = "okko", FuelTypeId = "qa-a95",
            SupplierPricePerLiter = 60m, FinalPricePerLiter = 72.34m, IsTestData = true,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        _context.FuelOffers.Add(offer);
        _context.FuelOfferVolumes.Add(new FuelOfferVolume
        {
            Id = "qa-vol-a95", OfferId = offer.Id, Liters = 10m, PackagePrice = 723.40m,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
    }
}
