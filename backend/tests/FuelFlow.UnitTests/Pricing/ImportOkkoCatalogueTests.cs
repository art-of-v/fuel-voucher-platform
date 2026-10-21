using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.ImportOkkoCatalogue;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// The catalogue import: OKKO's own published list, turned into fuels we do not carry yet. Step 4 of
/// #349 — the first write path in this area that nobody typed.
/// </summary>
/// <remarks>
/// <para>
/// Every test here is a rule about what the import must NOT do, because that is where the damage
/// would be: it must not price anything, must not archive anything, must not invent a grade we do not
/// sell, and must not treat an empty sheet as an empty catalogue. A test that only checked "it
/// created the fuel" would pass through all four mistakes.
/// </para>
/// <para>
/// The <c>OkkoPriceSyncService</c> calls it before pricing, because a fuel that does not exist yet has
/// nowhere to hold a pump price — so the ordering is part of the contract too, and is asserted there.
/// </para>
/// <para>
/// <b>These tests ran once.</b> The file was written to disk without a newline in it, so the first
/// <c>//</c> in it commented out the rest — class, tests and all — and the suite stayed green for
/// three weeks while none of this was checked. The shape of the file is now guarded
/// (<c>TestFileShapeTests</c>), and the fixtures speak the model the price actually lives in: the
/// price is on the FUEL, the sizes are <c>fuel_nominals</c> rows, and the pool is on the size.
/// </para>
/// </remarks>
public sealed class ImportOkkoCatalogueTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public ImportOkkoCatalogueTests()
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

    private ImportOkkoCatalogueCommandHandler Handler() =>
        new(_context, new BrandSlugResolver(_context), _events, new PriceHistoryRecorder(_context));

    private async Task SeedBrandAsync()
    {
        _context.AddMissing(BrandIds.Okko);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    /// <summary>
    /// The shape the scraper returns: the site's code, its price, and its own label for the grade.
    /// <para>
    /// The labels are deliberately the awkward ones the brands actually print — "ДП ЄВРО", "Пульс 100" —
    /// because they are renderings of a grade and not its identity. Nothing here may reach the catalogue
    /// as a name.
    /// </para>
    /// </summary>
    private static readonly OkkoPumpPrice[] PublishedSheet =
    [
        new("A-95", 92.90m, "А-95"),
        new("DP", 99.90m, "ДП ЄВРО"),
        new("Pulls 100", 102.90m, "Пульс 100"),   // aviation jet — a grade we do not sell
        new("AdBlue", 59.90m, "AdBlue")            // an additive, not a fuel
    ];

    [Fact]
    public async Task AGradeTheCatalogueDoesNotHave_BecomesAFuel()
    {
        await SeedBrandAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "tester");

        result.Created.Should().Be(2, "A-95 and diesel are grades we sell; jet fuel and AdBlue are not ours");
        result.CreatedFuels.Select(c => c.BrandFuelCode).Should().BeEquivalentTo(["A-95", "DP"]);

        var fuel = await _context.FuelTypes.SingleAsync(f => f.BrandFuelCode == "DP");
        fuel.BrandId.Should().Be(BrandIds.Okko);
        fuel.Name.Should().Be(
            "ДП",
            "the name comes from the grade the code identifies, not from the label the brand happened to "
            + "render — 'ДП ЄВРО' is one diesel spelled for a shelf, and a name that moves with a designer "
            + "is how '95' ended up beside '100 PULLS'");
        fuel.CreatedAtUtc.Should().NotBe(default);

        // The published pump price is the one figure the brand owns, and it lands on the fuel -
        // the price is the fuel's now, not a row's.
        fuel.PumpPricePerLiter.Should().Be(99.90m);
    }

    [Fact]
    public async Task ANewFuel_IsNotSellable_BecauseNoOneSetAMargin()
    {
        // The rule that keeps this import from publishing a price: a fuel with no margin has no
        // customer price at all, so it waits for an operator instead of going on sale at
        // cost-plus-zero.
        await SeedBrandAsync();

        await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "tester");

        var fuels = await _context.FuelTypes.ToListAsync();
        fuels.Should().NotBeEmpty();

        foreach (var fuel in fuels)
        {
            fuel.SupplierPricePerLiter.Should().BeNull("our cost is an operator's figure, not the brand's");
            fuel.MarginUahPerLiter.Should().BeNull();
            // Not "no price recorded" any more but "not priced at all": with no cost and no margin the
            // derived price is null, which is what makes the fuel unsellable.
            FuelPricing.FinalPerLiterOrNull(
                fuel.SupplierPricePerLiter, fuel.MarginUahPerLiter).Should().BeNull();
        }
    }

    [Fact]
    public async Task ANewFuel_GetsTheSizesTheOtherImportersGiveIt()
    {
        // The sizes a created grade is sold in, in the real pool: the pool lives on the size, so
        // this is where "which pool is this fuel in" is answered for a fuel the import created.
        await SeedBrandAsync();

        await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "tester");

        var fuel = await _context.FuelTypes.SingleAsync(f => f.BrandFuelCode == "DP");
        var sizes = await _context.FuelNominals
            .Where(n => n.FuelTypeId == fuel.Id)
            .ToListAsync();

        sizes.Select(n => (int)n.Liters).Should().BeEquivalentTo([10, 20, 50]);
        sizes.Should().OnlyContain(n => n.DataKind == DataKinds.Real);
    }

    [Fact]
    public async Task ASecondRun_CreatesNothing()
    {
        // The job runs every fifteen minutes. An import that created a duplicate each time would turn
        // the catalogue into the twin problem #349 was opened for.
        await SeedBrandAsync();

        await Handler().ApplyAsync(new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");
        var second = await Handler().ApplyAsync(new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");

        second.Created.Should().Be(0);
        second.AlreadyKnown.Should().Be(2);

        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Okko)).Should().Be(2);
    }

    [Fact]
    public async Task AnExistingFuel_IsLeftExactlyAsItWas()
    {
        // Operator columns belong to the operator: our cost, our margin, the below-cost opt-in and
        // the archive flag. An import that "helpfully" refreshed a price would be publishing a
        // number no human approved.
        await SeedBrandAsync();
        var existing = new FuelTypeEntity
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.Okko,
            Name = "А-95 (renamed by hand)",
            BrandFuelCode = "A-95",
            BasePrice = 54m,
            DiscountPrice = 51m,
            // The price is the fuel's: the pump the operator set and the margin he set with it.
            PumpPricePerLiter = 60m,
            SupplierPricePerLiter = 49m,
            MarginUahPerLiter = 2m, // 49 + 2 = the 51 the assertion below expects; the column price (60) is a reference
            AllowBelowCost = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.FuelTypes.Add(existing);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");

        result.AlreadyKnown.Should().Be(1);

        var reloaded = await _context.FuelTypes.SingleAsync(f => f.Id == existing.Id);
        reloaded.Name.Should().Be("А-95 (renamed by hand)");
        reloaded.DiscountPrice.Should().Be(51m);
        reloaded.AllowBelowCost.Should().BeTrue();

        // The pump price the operator set is still the operator's: the pump sync owns the pump,
        // and this import owns nothing here.
        reloaded.PumpPricePerLiter.Should().Be(60m);
        FuelPricing.FinalPerLiterOrNull(
            reloaded.SupplierPricePerLiter, reloaded.MarginUahPerLiter).Should().Be(51m);
    }

    [Fact]
    public async Task AGradeMissingFromTheSite_IsNeverArchived()
    {
        // The absence rule. A scrape that fails to read a page, an anti-bot interstitial, a locale
        // change — every one of them would otherwise empty the catalogue of live fuels that paid
        // vouchers point at.
        await SeedBrandAsync();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.Okko,
            Name = "ДП Застарілий",
            BrandFuelCode = "DP-OLD",
            BasePrice = 50m,
            DiscountPrice = 48m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await Handler().ApplyAsync(new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");

        var stale = await _context.FuelTypes.SingleAsync(f => f.BrandFuelCode == "DP-OLD");
        stale.IsDeleted.Should().BeFalse("not on the site today is not off the catalogue — an operator decides that");
    }

    [Fact]
    public async Task AnEmptySheet_IsRefusedRatherThanApplied()
    {
        // Zero rows must never be able to mean "the catalogue is now empty".
        await SeedBrandAsync();

        var act = async () => await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand([], DryRun: false), Guid.Empty, "t");

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an empty sheet is a fetch that failed, not a catalogue to empty");
    }

    [Fact]
    public async Task AGradeWeDoNotSell_IsReportedAndNeverCreated()
    {
        await SeedBrandAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");

        result.UnclassifiedSiteFuels.Should().BeEquivalentTo(["Pulls 100", "AdBlue"]);

        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Okko)).Should().Be(2);
        (await _context.FuelTypes.AnyAsync(f => f.Name.Contains("100"))).Should().BeFalse(
            "inventing a fuel the catalogue has no category for is how a supplier's new product becomes a sellable row");
    }

    [Fact]
    public async Task AGradeWithNoLabel_StillGetsItsCanonicalName()
    {
        // The name comes from the grade now, not from the site's label, so a payload that carried no
        // label is no longer a reason to refuse a grade the brand clearly sells. That used to end in
        // "reported and nothing created", which meant a missing field on their side silently removed a
        // fuel from our catalogue.
        await SeedBrandAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand([new OkkoPumpPrice("DP", 99.90m)], DryRun: false), Guid.Empty, "t");

        result.Created.Should().Be(1);
        result.CreatedFuels.Single().Name.Should().Be(
            "ДП", "the name follows the code, which is the identity; the label was only ever a rendering");

        (await _context.FuelTypes.SingleAsync()).BrandFuelCode.Should().Be("DP");
    }

    [Fact]
    public async Task AGradeWeAlreadySellUnderOurOwnName_IsReportedAndNeverDuplicated()
    {
        // The production incident: matching on the code alone found nothing because nothing carried a
        // code, and the import added "ДП" beside the "ДП PULLS" that already meant the same diesel.
        // Grade is the identity, so the second row is impossible by construction.
        await SeedBrandAsync();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = Guid.NewGuid(), Name = "ДП PULLS", BrandId = BrandIds.Okko, CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand([new OkkoPumpPrice("Pulls Diesel", 102.90m, "ДП")], DryRun: false),
            Guid.Empty, "t");

        result.Created.Should().Be(0);
        result.GradesHeld.Should().ContainSingle();
        result.GradesHeld[0].SiteFuelCode.Should().Be("Pulls Diesel");
        result.GradesHeld[0].OurFuelName.Should().Be("ДП PULLS");

        (await _context.FuelTypes.Select(f => f.Name).ToListAsync())
            .Should().BeEquivalentTo(["ДП PULLS"], "one row per grade, whatever either side calls it");
    }

    [Fact]
    public async Task ASecondSpellingOfOneGrade_IsTheSameFuel_NotANewRow()
    {
        // "ДП" and "DP" are one diesel written two ways. Our own catalogue holds both spellings, and the
        // site alternates between them, so a code check alone would keep adding rows forever.
        await SeedBrandAsync();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = Guid.NewGuid(), Name = "DP", BrandId = BrandIds.Okko,
            BrandFuelCode = "DP", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand([new OkkoPumpPrice("DP", 99.90m, "ДП ЄВРО")], DryRun: false),
            Guid.Empty, "t");

        result.Created.Should().Be(0);
        (await _context.FuelTypes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ADryRun_CreatesNothingAndStillSaysWhatItWouldDo()
    {
        await SeedBrandAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: true), Guid.Empty, "t");

        result.DryRun.Should().BeTrue();
        result.Created.Should().Be(2, "the preview is the same answer without the writes");
        result.CreatedFuels.Select(c => c.Name).Should().BeEquivalentTo(["А-95", "ДП"]);

        (await _context.FuelTypes.AnyAsync()).Should().BeFalse();
        (await _context.FuelNominals.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task EveryCreatedFuel_IsRecordedInTheAuditTrail()
    {
        // A catalogue that grew while nobody was watching has to say so where a typed one does.
        await SeedBrandAsync();

        await Handler().ApplyAsync(new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");
        _context.ChangeTracker.Clear();

        var events = await _context.AuditEventOutbox
            .Where(e => e.EventType == "FuelCreatedByImport")
            .ToListAsync();

        events.Should().HaveCount(2);
        events.Should().OnlyContain(e => e.ScopeId == "okko");
        events.Select(e => e.Summary).Should().OnlyContain(s => s.Contains("until a margin is set"));
    }

    [Fact]
    public async Task AnArchivedFuel_DoesNotBlockAFreshOneForTheSameGrade()
    {
        await SeedBrandAsync();
        var archivedId = Guid.NewGuid();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = archivedId,
            BrandId = BrandIds.Okko,
            Name = "А-95 (archived)",
            BrandFuelCode = "A-95",
            IsDeleted = true,
            DeletedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand([new OkkoPumpPrice("A-95", 92.90m, "А-95")], DryRun: false), Guid.Empty, "t");

        result.Created.Should().Be(1, "the archived row is history, not an answer");
        result.CreatedFuels.Single().FuelTypeId.Should().NotBe(archivedId);
    }
}
