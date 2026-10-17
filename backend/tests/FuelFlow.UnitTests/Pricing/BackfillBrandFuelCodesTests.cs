using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.BackfillBrandFuelCodes;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// Matches the fuels an operator typed to the codes the brand publishes. Step 6b of #349 — the last
/// piece that lets the catalogue and the price import match by code rather than by name.
/// </summary>
/// <remarks>
/// <para>
/// This is a derivation, so most of these tests are about the cases where it must <em>not</em> act: a
/// grade with several published codes, a grade with none, a candidate another fuel already holds. A
/// wrong code is not a cosmetic mistake — <c>ux_fuel_types_station_brand_fuel_code</c> then enforces
/// it, and the price import hands that fuel somebody else's pump.
/// </para>
/// <para>
/// OKKO's own list only. UPG publishes one too (<c>scripts/fetch-upg-wog-prices.mjs</c>) and WOG's was
/// there once and gone the next request, so a backfill built on an unreliable source would fill codes
/// on a bad day and never revisit them.
/// </para>
/// </remarks>
public sealed class BackfillBrandFuelCodesTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public BackfillBrandFuelCodesTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _events = new AuditEventService(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private BackfillBrandFuelCodesCommandHandler Handler() => new(_context, _events);

    /// <summary>What OKKO publishes, in the shape the fetch returns it.</summary>
    private static readonly OkkoPumpPrice[] Published =
    [
        new("A-95", 92.90m, "А-95"),
        new("Pulls 95", 95.90m, "Pulls 95"),
        new("DP", 99.90m, "ДП ЄВРО"),
        new("Pulls Diesel", 102.90m, "ДП PULLS"),
        new("SPBT", 46.90m, "ГАЗ"),
        new("Pulls 100", 102.90m, "Пульс 100"),   // a grade we do not sell
    ];

    private async Task SeedAsync(params FuelTypeEntity[] fuels)
    {
        _context.Stations.Add(new Station { Id = "okko", Name = "OKKO", Color = "#000", LogoText = "OKKO" });
        foreach (var fuel in fuels)
        {
            fuel.StationId = "okko";
            fuel.CreatedAtUtc = DateTime.UtcNow;
            fuel.UpdatedAtUtc = DateTime.UtcNow;
            _context.FuelTypes.Add(fuel);
        }

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private static FuelTypeEntity Fuel(string id, string name, string? code = null)
        => new() { Id = id, Name = name, StationId = "okko", BrandFuelCode = code, BasePrice = 50m, DiscountPrice = 48m };

    [Fact]
    public async Task AHandTypedFuel_GetsTheCodeTheBrandPublishesForItsGrade()
    {
        await SeedAsync(Fuel("guid-a", "А95 ЄВРО"), Fuel("guid-b", "ГАЗ"));

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand(Published, DryRun: false), Guid.Empty, "t");

        result.Filled.Should().Be(2);
        (await _context.FuelTypes.SingleAsync(f => f.Id == "guid-a")).BrandFuelCode.Should().Be("A-95");
        (await _context.FuelTypes.SingleAsync(f => f.Id == "guid-b")).BrandFuelCode.Should().Be("SPBT");
    }

    [Fact]
    public async Task AFuelThatAlreadyHasACode_IsNotTouched()
    {
        // Including one whose code is WRONG for its grade: this backfill fills an empty column, and a
        // human who set a code by hand knew something the sheet does not. Overwriting it would make
        // every run of this job an argument with the last operator's decision.
        await SeedAsync(Fuel("coded", "А95 ЄВРО", code: "A-95"), Fuel("hand-set", "ДП ЄВРО", code: "MY OWN"));

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand(Published, DryRun: false), Guid.Empty, "t");

        result.Filled.Should().Be(0);
        (await _context.FuelTypes.SingleAsync(f => f.Id == "hand-set")).BrandFuelCode.Should().Be("MY OWN");
    }

    [Fact]
    public async Task AGradeWithNoPublishedCode_IsReported_AndLeftAlone()
    {
        // "А 92" is a real fuel at OKKO and is not on the list this fetcher parses. Inventing a code
        // for it would be the guess that #1189's migration refused to make.
        await SeedAsync(Fuel("a92", "А 92"));

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand(Published, DryRun: false), Guid.Empty, "t");

        result.Filled.Should().Be(0);
        result.Skipped.Should().ContainSingle(s => s.Name == "А 92");
        (await _context.FuelTypes.SingleAsync(f => f.Id == "a92")).BrandFuelCode.Should().BeNull();
    }

    [Fact]
    public async Task ACodeTheClassifierDoesNotKnow_IsNotACandidate()
    {
        // The published list can carry entries this catalogue does not sell, and the vendor's own names
        // for them are not in the classifier's map — "DP EURO" means diesel to OKKO and nothing to us.
        // Treating an unmapped code as a candidate would hand the fuel a code the price step cannot
        // read back, which is worse than leaving it empty.
        await SeedAsync(Fuel("dp", "ДП ЄВРО"));
        var sheet = new[]
        {
            new OkkoPumpPrice("DP", 99.90m, "ДП ЄВРО"),
            new OkkoPumpPrice("DP EURO", 100.90m, "ДП ЄВРО"),
            new OkkoPumpPrice("AdBlue", 59.90m, "AdBlue"),
        };

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand(sheet, DryRun: false), Guid.Empty, "t");

        result.Filled.Should().Be(1);
        result.Fills.Single().Code.Should().Be("DP", "the one code this catalogue recognises for diesel");
    }

    [Fact]
    public async Task ACodeAnotherLiveFuelHolds_IsNotStolen()
    {
        // The seeded okko-95 already holds "A-95". A second A-95 must not take it, and must not take
        // the seeded fuel's code either — both are refused by the index, and the point of a report is
        // that somebody reads which one it was.
        await SeedAsync(Fuel("seeded", "A-95", code: "A-95"), Fuel("second-a95", "А95 ЄВРО"));

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand(Published, DryRun: false), Guid.Empty, "t");

        result.Filled.Should().Be(0);
        result.Skipped.Should().ContainSingle(s => s.Candidates == "A-95");
    }

    [Fact]
    public async Task TwoFuelsOfOneGrade_OneGetsTheCodeAndTheOtherIsReported()
    {
        // The case the unique index exists for. The first by id order takes the code and the second is
        // named in the report — which is how a human learns to archive one of them, or to buy the other
        // paper so there is a second code to choose from.
        await SeedAsync(Fuel("first", "А95 ЄВРО"), Fuel("second", "А-95"));

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand(Published, DryRun: false), Guid.Empty, "t");

        result.Filled.Should().Be(1);
        result.Skipped.Should().ContainSingle(s => s.Name == "А-95");
    }

    [Fact]
    public async Task AnArchivedFuel_IsNeitherCodedNorCounted()
    {
        // An archived fuel is history. Coding it would add a second answer to "which fuel does this code
        // mean", and the index is partial on NOT is_deleted, so nothing would stop both.
        var archived = Fuel("old-a95", "А95 ЄВРО");
        archived.IsDeleted = true;
        archived.DeletedAtUtc = DateTime.UtcNow;

        await SeedAsync(archived, Fuel("live-a95", "А95 ЄВРО"));

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand(Published, DryRun: false), Guid.Empty, "t");

        result.Filled.Should().Be(1);
        result.Fills.Single().FuelTypeId.Should().Be("live-a95");

        // IgnoreQueryFilters because fuel_types has a global !IsDeleted filter — which is itself part
        // of why the handler's explicit !IsDeleted is belt and braces rather than decoration.
        (await _context.FuelTypes.IgnoreQueryFilters().SingleAsync(f => f.Id == "old-a95"))
            .BrandFuelCode.Should().BeNull();
    }

    [Fact]
    public async Task ADryRun_ReportsWithoutWriting()
    {
        await SeedAsync(Fuel("guid-a", "А95 ЄВРО"));

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand(Published, DryRun: true), Guid.Empty, "t");

        result.DryRun.Should().BeTrue();
        result.Fills.Should().ContainSingle(f => f.Code == "A-95");

        (await _context.FuelTypes.SingleAsync(f => f.Id == "guid-a")).BrandFuelCode.Should().BeNull();
    }

    [Fact]
    public async Task EveryFilledFuel_IsRecordedInTheAuditTrail()
    {
        await SeedAsync(Fuel("guid-a", "А95 ЄВРО"));

        await Handler().ApplyAsync(new BackfillBrandFuelCodesCommand(Published, DryRun: false), Guid.Empty, "t");
        _context.ChangeTracker.Clear();

        var events = await _context.AuditEventOutbox
            .Where(e => e.EventType == "BrandFuelCodeBackfilled")
            .ToListAsync();

        events.Should().ContainSingle();
        events[0].Summary.Should().Contain("A-95");
        events[0].ScopeId.Should().Be("okko");
    }

    [Fact]
    public async Task AnEmptySheet_FillsNothing()
    {
        // The fetch already refuses an empty page, but a handler that fills codes from nothing would be
        // a silent no-op that looks like "nothing to do" rather than "the source failed".
        await SeedAsync(Fuel("guid-a", "А95 ЄВРО"));

        var result = await Handler().ApplyAsync(
            new BackfillBrandFuelCodesCommand([], DryRun: false), Guid.Empty, "t");

        result.Filled.Should().Be(0);
        (await _context.FuelTypes.SingleAsync(f => f.Id == "guid-a")).BrandFuelCode.Should().BeNull();
    }
}
