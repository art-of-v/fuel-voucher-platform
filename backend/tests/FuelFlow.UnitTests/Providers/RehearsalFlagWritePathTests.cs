using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Features.Pricing;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.AdminAddFuel;
using FuelFlow.Features.Brands.AdminDeleteFuel;
using FuelFlow.Features.Brands.AdminUpdateFuel;
using FuelFlow.Features.Brands.AdminUpdateNominals;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

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
    private readonly BrandFuelController _controller;

    public RehearsalFlagWritePathTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        // The controller asks a handler for the write; the handler owns the database and the audit
        // trail. They all share this one context, exactly as the controller used to.
        var events = new AuditEventService(_context);
        var prices = new PriceHistoryRecorder(_context);
        _controller = new BrandFuelController(
        new FuelFlow.Features.Brands.GetFuelPriceHistory.GetFuelPriceHistoryQueryHandler(_context),
            new GetFuelPriceAtQueryHandler(_context),
            new AdminAddFuelCommandHandler(_context, events, prices),
            new AdminUpdateFuelCommandHandler(_context, events, prices),
            new AdminDeleteFuelCommandHandler(_context, events),
            new AdminUpdateNominalsCommandHandler(_context, events));

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

        _context.Brands.Add(new Brand
        {
            Id = BrandIds.Okko, Slug = "okko", Name = "okko", Color = "#22c55e", LogoText = "okko",
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
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
            ToUuid("qa-a95"),
            new BrandFuelDto
            {
                Id = "qa-a95",
                Name = "А95 ЄВРО",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                AllowBelowCost = false,
                // IsTestData deliberately absent.
                IsTestData = null
            },
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        CodeOf(bad).Should().Be(BrandRefusalCodes.TestDataRequired,
            "the panel picks a sentence by code, and the sentence is in its own language");

          var sizes = await _context.FuelNominals.Where(n => n.FuelTypeId == ToUuid("qa-a95")).ToListAsync();
        sizes.Should().OnlyContain(n => n.DataKind == DataKinds.Qa,
            "an edit that does not say must not be able to put rehearsal stock on sale");
    }

    [Fact]
    public async Task AddFuel_WithoutTheFlag_IsRefused()
    {
        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = null
            },
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        CodeOf(bad).Should().Be(BrandRefusalCodes.TestDataRequired);
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
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = name,
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = false
            },
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().NotBeNull();
        var problem = bad.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be(BrandRefusalCodes.RehearsalName,
            "the panel needs a code to say this in the operator's language");
        problem.Extensions["token"].Should().NotBeNull(
            "the sentence names the word in the fuel's name that reads as rehearsal");
    }

    [Fact]
    public async Task ARehearsalLookingFuel_IsAccepted_WhenTheFlagSaysRehearsal()
    {
        // The guard must not make rehearsal naming impossible - the seeds name rows exactly like this.
        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО (ТЕСТ 1ГРН)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = true
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>(
            "the flag is the control; the name is only a second opinion");
    }

    [Fact]
    public async Task AnOrdinaryFuel_IsUnaffected()
    {
        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = false
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();
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
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = name,
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = false
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>(
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
    /// <c>ux_fuel_types_Brand_name</c>; this is the version that can say which fuel already owns the
    /// name, and it is asserted here on an in-memory context that has no index at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AddingAFuelThatAlreadyExistsAtThatBrand_IsRefused()
    {
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync(); // the guard reads the row, not the change tracker

        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = true
            },
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;

        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Okko && f.Name == "А95 ЄВРО(QA)"))
            .Should().Be(1, "a second row for the same fuel the storefront would list twice");
        (await _context.FuelNominals.CountAsync(n => n.FuelTypeId == ToUuid("qa-a95")))
            .Should().Be(1, "and it must not have created a second size for the same pool");
        conflict.Value.Should().NotBeNull("the operator is told which fuel to reprice instead");
        CodeOf(conflict).Should().Be(BrandRefusalCodes.NameExists);
    }

    [Fact]
    public async Task ASecondSpellingOfTheSameFuel_IsRefused_AndTheWinnerIsNamed()
    {
        // The owner's decision (planning #349, 2026-10-21): "А-95" and "A-95" are one grade, and the
        // catalogue says so once. The index on the name cannot tell them apart - it is on the name,
        // and the name is what a person typed - so the write path asks the grade key instead, and
        // says which of the two rows already exists.
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync();

        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "A95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = true
            },
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;

        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Okko))
            .Should().Be(1, "a second row for the same grade is what the storefront lists twice");
        (await _context.FuelTypes.CountAsync(f => f.Name == "A95 ЄВРО(QA)"))
            .Should().Be(0, "the second spelling did not land");

        // The operator is told WHICH row won, and what to do about it - that is the part the issue
        // left open, and a refusal that names nothing is a refusal the operator cannot act on.
        conflict.Value.Should().NotBeNull("the refusal says something");
        var payload = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        payload.Extensions["code"].Should().Be(BrandRefusalCodes.GradeExists);
        payload.Extensions["fuelTypeId"].Should().Be(ToUuid("qa-a95"),
            "the operator is told which fuel already holds the grade");
        payload.Extensions["twinName"].Should().Be("А95 ЄВРО(QA)",
            "and the sentence it drives names the row in the operator's language");
        payload.Detail.Should().Contain("А95 ЄВРО(QA)", "the refusal names the row that won");
    }

    [Fact]
    public async Task TheSameFuelNameAtAnotherBrand_IsNotADuplicate()    {
        // The uniqueness is per Brand, deliberately: two networks both listing 'ГАЗ' is the
        // catalogue working, not a duplicate.
        await SeedQaFuelAsync();

        _context.Brands.Add(new Brand
        {
            Id = BrandIds.Wog, Slug = "wog", Name = "wog", Color = "#10b981", LogoText = "wog",
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _controller.AddFuel(
            BrandIds.Wog,
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = true
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task AnArchivedFuel_FreesItsNameForAFreshOne()
    {
        // The write path's half of the partial index: what the admin screen archives is not a
        // candidate for "already exists", so a catalogue can be rebuilt under the same names.
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync();

        var archived = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("qa-a95"));
        archived.IsDeleted = true;
        archived.DeletedAtUtc = TestClock.UtcNow;
        await _context.SaveChangesAsync();

        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = true
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task AddingAFuelWhoseCodeIsTaken_IsRefused()
    {
        // The other half of a fuel's identity, refused in the same shape as the name. A code identifies
        // one fuel per network, so two fuels claiming one is what a catalogue import would resolve
        // arbitrarily - and the database's answer (23505) names neither fuel.
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync();

        var existing = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("qa-a95"));
        existing.BrandFuelCode = "9018";
        await _context.SaveChangesAsync();

        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "ДП (code guard test)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = true,
                BrandFuelCode = "9018"
            },
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().NotBeNull("the operator is told which fuel already holds the code");
        CodeOf(conflict).Should().Be(BrandRefusalCodes.CodeTaken);

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

        var existing = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("qa-a95"));
        existing.BrandFuelCode = "9018";
        await _context.SaveChangesAsync();

        await _controller.UpdateFuel(
            ToUuid("qa-a95"),
            new BrandFuelDto
            {
                Id = "qa-a95",
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                AllowBelowCost = false,
                IsTestData = true
                // BrandFuelCode deliberately absent.
            },
            CancellationToken.None);

        var reloaded = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("qa-a95"));
        reloaded.BrandFuelCode.Should().Be("9018");
    }

    [Fact]
    public async Task AnEmptyCode_ClearsIt()
    {
        await SeedQaFuelAsync();
        await _context.SaveChangesAsync();

        var existing = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("qa-a95"));
        existing.BrandFuelCode = "9018";
        await _context.SaveChangesAsync();

        await _controller.UpdateFuel(
            ToUuid("qa-a95"),
            new BrandFuelDto
            {
                Id = "qa-a95",
                Name = "А95 ЄВРО(QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                AllowBelowCost = false,
                IsTestData = true,
                BrandFuelCode = ""
            },
            CancellationToken.None);

        var reloaded = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("qa-a95"));
        reloaded.BrandFuelCode.Should().BeNull();
    }

    /// <summary>
    /// The flag decides the pool the sizes are written in, and the pool is what the storefront
    /// filters on. A rehearsal grade added through this screen used to be created in the LIVE pool:
    /// the flag was read, the pool was computed, and the sizes were written without it — so the
    /// rehearsal fuel was listed on the public shelf (planning #460).
    /// </summary>
    [Fact]
    public async Task ARehearsalFuel_IsCreatedInTheQaPool()
    {
        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "А95 ЄВРО (QA)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = true
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.Name == "А95 ЄВРО (QA)");
        var sizes = await _context.FuelNominals.Where(n => n.FuelTypeId == fuel.Id).ToListAsync();

        sizes.Should().NotBeEmpty("a fuel with no sizes is not a fuel the storefront can sell");
        sizes.Should().OnlyContain(n => n.DataKind == DataKinds.Qa,
            "the pool is part of a size's identity, and it is what the storefront filters on");
    }

    [Fact]
    public async Task ALiveFuel_IsCreatedInTheLivePool()
    {
        var result = await _controller.AddFuel(
            BrandIds.Okko,
            new CreateFuelRequest
            {
                Name = "ДП ЄВРО",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                IsTestData = false
            },
            CancellationToken.None);

        result.Should().BeOfType<CreatedResult>();

        var fuel = await _context.FuelTypes.SingleAsync(f => f.Name == "ДП ЄВРО");
        var sizes = await _context.FuelNominals.Where(n => n.FuelTypeId == fuel.Id).ToListAsync();

        sizes.Should().NotBeEmpty();
        sizes.Should().OnlyContain(n => n.DataKind == DataKinds.Real);
    }

    /// <summary>
    /// The pool of a fuel that already has sizes is not a price edit's to change. The screen sends
    /// the flag the fuel reads back with, so an ordinary edit agrees; an operator who flips it is
    /// asking for the pool to change, and the answer says so rather than quietly doing nothing —
    /// a silent no-op here is how somebody comes to believe a live fuel was hidden.
    /// </summary>
    [Fact]
    public async Task ARepricing_ThatFlipsThePool_IsRefused_AndTheSizesKeepTheirPool()
    {
        await SeedLiveFuelAsync();

        var result = await _controller.UpdateFuel(
            ToUuid("live-dp"),
            new BrandFuelDto
            {
                Id = "live-dp",
                Name = "ДП ЄВРО",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                AllowBelowCost = false,
                IsTestData = true
            },
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>(
            "the sizes decide the pool, and a price edit does not move them").Subject;
        CodeOf(conflict).Should().Be(BrandRefusalCodes.PoolMismatch);

        var sizes = await _context.FuelNominals.Where(n => n.FuelTypeId == ToUuid("live-dp")).ToListAsync();
        sizes.Should().OnlyContain(n => n.DataKind == DataKinds.Real);
    }

    [Fact]
    public async Task AFuelWithNoSizes_IsSeededInThePoolTheFlagNames()
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("empty-dp"), BrandId = BrandIds.Okko, Name = "ДП ЄВРО (empty)",
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _controller.UpdateFuel(
            ToUuid("empty-dp"),
            new BrandFuelDto
            {
                Id = "empty-dp",
                Name = "ДП ЄВРО (empty)",
                SupplierPricePerLiter = 60m,
                MarginUahPerLiter = 12.34m,
                PumpPricePerLiter = 92.90m,
                AllowBelowCost = false,
                IsTestData = true
            },
            CancellationToken.None);

        result.Should().NotBeOfType<ConflictObjectResult>("a fuel with no sizes has no pool to disagree with");

        var sizes = await _context.FuelNominals.Where(n => n.FuelTypeId == ToUuid("empty-dp")).ToListAsync();
        sizes.Should().NotBeEmpty("the defaults are seeded");
        sizes.Should().OnlyContain(n => n.DataKind == DataKinds.Qa);
    }

    /// <summary>
    /// A live fuel with a live size - the mirror image of the rehearsal one above.
    /// </summary>
    private async Task SeedLiveFuelAsync()
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("live-dp"), BrandId = BrandIds.Okko, Name = "ДП ЄВРО",
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
        });
        _context.FuelNominals.Add(new FuelNominal
        {
            FuelTypeId = ToUuid("live-dp"), Liters = 10m, DataKind = DataKinds.Real,
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedQaFuelAsync()
    {
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("qa-a95"), BrandId = BrandIds.Okko, Name = "А95 ЄВРО(QA)",
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
        });
        // The rehearsal flag lives on the NOMINAL now - the pool is part of the size's identity, and it is
        // what the storefront filters on. The size is here because a fuel with no nominal on it is
        // not a fuel the storefront can sell, and these assertions are about what the admin screens
        // read back.
        _context.FuelNominals.Add(new FuelNominal
        {
            FuelTypeId = ToUuid("qa-a95"), Liters = 10m, DataKind = DataKinds.Qa,
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
        });
    }

    /// <summary>
    /// The machine code on a refusal body. The admin panel's sentence is chosen by this, so a
    /// refusal that arrives without one reads on screen in the server's English.
    /// </summary>
    private static object? CodeOf(ObjectResult result)
    {
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        return problem.Extensions.TryGetValue("code", out var code) ? code : null;
    }
}
