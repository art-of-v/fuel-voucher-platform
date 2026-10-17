using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ImportOkkoCatalogue;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
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

    private ImportOkkoCatalogueCommandHandler Handler() => new(_context, _events, new OfferPriceRecorder(_context));

    private async Task SeedStationAsync()
    {
        _context.Stations.Add(new Station { Id = "okko", Name = "OKKO", Color = "#000", LogoText = "OKKO" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    /// <summary>The shape the scraper returns: the site's code, its price, and its own label for the grade.</summary>
    private static readonly OkkoPumpPrice[] PublishedSheet =
    [
        new("A-95", 92.90m, "А-95"),
        new("DP", 99.90m, "ДП ЄВРО"),
        new("Pulls 100", 102.90m, "Пульс 100"),   // aviation jet — a grade we do not sell
        new("AdBlue", 59.90m, "AdBlue")            // an additive, not a fuel
    ];

    [Fact]
    public async Task AGradeTheCatalogueDoesNotHave_BecomesAFuelAndAnOffer()
    {
        await SeedStationAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "tester");

        result.Created.Should().Be(2, "A-95 and ДП ЄВРО are grades we sell; jet fuel and AdBlue are not ours");
        result.CreatedFuels.Select(c => c.BrandFuelCode).Should().BeEquivalentTo(["A-95", "DP"]);

        var fuel = await _context.FuelTypes.SingleAsync(f => f.BrandFuelCode == "DP");
        fuel.StationId.Should().Be("okko");
        fuel.Name.Should().Be("ДП ЄВРО", "named the way the brand names it, not the way a code reads");
        fuel.CreatedAtUtc.Should().NotBe(default);

        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == fuel.Id);
        offer.PumpPricePerLiter.Should().Be(99.90m);
        offer.DataKind.Should().Be(DataKinds.Real);
    }

    [Fact]
    public async Task ANewFuel_IsNotSellable_BecauseNoOneSetAMargin()
    {
        // The rule that keeps this import from publishing a price: an offer with no volumes is not
        // listed by the storefront and cannot be bought, so the fuel waits for an operator instead of
        // going on sale at cost-plus-zero.
        await SeedStationAsync();

        await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "tester");

        var offers = await _context.FuelOffers.ToListAsync();
        offers.Should().NotBeEmpty();

        foreach (var offer in offers)
        {
            offer.SupplierPricePerLiter.Should().BeNull("our cost is an operator's figure, not the brand's");
            offer.MarginUahPerLiter.Should().BeNull();
            offer.FinalPricePerLiter.Should().BeNull();

            (await _context.FuelOfferVolumes.CountAsync(v => v.OfferId == offer.Id))
                .Should().Be(0, "no nominals means nothing is displayed and nothing is purchasable");
        }
    }

    [Fact]
    public async Task ASecondRun_CreatesNothing()
    {
        // The job runs every fifteen minutes. An import that created a duplicate each time would turn
        // the catalogue into the twin problem #349 was opened for.
        await SeedStationAsync();

        await Handler().ApplyAsync(new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");
        var second = await Handler().ApplyAsync(new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");

        second.Created.Should().Be(0);
        second.AlreadyKnown.Should().Be(2);

        (await _context.FuelTypes.CountAsync(f => f.StationId == "okko")).Should().Be(2);
    }

    [Fact]
    public async Task AnExistingFuel_IsLeftExactlyAsItWas()
    {
        // Operator columns belong to the operator: our price, the margin, the discount floor, the
        // below-cost opt-in, and the archive flag. An import that "helpfully" refreshed a price would
        // be publishing a number no human approved.
        await SeedStationAsync();
        var existing = new FuelTypeEntity
        {
            Id = "okko-95",
            StationId = "okko",
            Name = "А-95 (renamed by hand)",
            BrandFuelCode = "A-95",
            BasePrice = 54m,
            DiscountPrice = 51m,
            AllowBelowCost = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.FuelTypes.Add(existing);
        _context.FuelOffers.Add(new FuelOffer
        {
            Id = "offer-95",
            StationId = "okko",
            FuelTypeId = "okko-95",
            DataKind = DataKinds.Real,
            SupplierPricePerLiter = 49m,
            MarginUahPerLiter = 2m,
            FinalPricePerLiter = 51m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");

        result.AlreadyKnown.Should().Be(1);

        var reloaded = await _context.FuelTypes.SingleAsync(f => f.Id == "okko-95");
        reloaded.Name.Should().Be("А-95 (renamed by hand)");
        reloaded.DiscountPrice.Should().Be(51m);
        reloaded.AllowBelowCost.Should().BeTrue();

        var offer = await _context.FuelOffers.SingleAsync(o => o.FuelTypeId == "okko-95");
        offer.SupplierPricePerLiter.Should().Be(49m, "the pump sync owns the pump; this import owns nothing here");
        offer.FinalPricePerLiter.Should().Be(51m);
    }

    [Fact]
    public async Task AGradeMissingFromTheSite_IsNeverArchived()
    {
        // The absence rule. A scrape that fails to read a page, an anti-bot interstitial, a locale
        // change — every one of them would otherwise empty the catalogue of live fuels that paid
        // vouchers point at.
        await SeedStationAsync();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-old",
            StationId = "okko",
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

        var stale = await _context.FuelTypes.SingleAsync(f => f.Id == "okko-old");
        stale.IsDeleted.Should().BeFalse("not on the site today is not off the catalogue — an operator decides that");
    }

    [Fact]
    public async Task AnEmptySheet_IsRefusedRatherThanApplied()
    {
        // Zero rows must never be able to mean "the catalogue is now empty".
        await SeedStationAsync();

        var act = async () => await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand([], DryRun: false), Guid.Empty, "t");

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an empty sheet is a fetch that failed, not a catalogue to empty");
    }

    [Fact]
    public async Task AGradeWeDoNotSell_IsReportedAndNeverCreated()
    {
        await SeedStationAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: false), Guid.Empty, "t");

        result.UnclassifiedSiteFuels.Should().BeEquivalentTo(["Pulls 100", "AdBlue"]);

        (await _context.FuelTypes.CountAsync(f => f.StationId == "okko")).Should().Be(2);
        (await _context.FuelTypes.AnyAsync(f => f.Name.Contains("100"))).Should().BeFalse(
            "inventing a fuel the catalogue has no category for is how a supplier's new product becomes a sellable row");
    }

    [Fact]
    public async Task AGradeWithNoLabel_IsReportedAndNotNamedAfterItsCode()
    {
        // "DP" on the storefront is a worse name for a customer than the one OKKO prints, so a payload
        // without labels yields a report rather than a fuel.
        await SeedStationAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand([new OkkoPumpPrice("DP", 99.90m)], DryRun: false), Guid.Empty, "t");

        result.Created.Should().Be(0);
        result.UnnamedSiteFuels.Should().BeEquivalentTo(["DP"]);

        (await _context.FuelTypes.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ADryRun_CreatesNothingAndStillSaysWhatItWouldDo()
    {
        await SeedStationAsync();

        var result = await Handler().ApplyAsync(
            new ImportOkkoCatalogueCommand(PublishedSheet, DryRun: true), Guid.Empty, "t");

        result.DryRun.Should().BeTrue();
        result.Created.Should().Be(2, "the preview is the same answer without the writes");
        result.CreatedFuels.Select(c => c.Name).Should().BeEquivalentTo(["А-95", "ДП ЄВРО"]);

        (await _context.FuelTypes.AnyAsync()).Should().BeFalse();
        (await _context.FuelOffers.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task EveryCreatedFuel_IsRecordedInTheAuditTrail()
    {
        // A catalogue that grew while nobody was watching has to say so where a typed one does.
        await SeedStationAsync();

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
        await SeedStationAsync();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "okko-95-old",
            StationId = "okko",
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
        result.CreatedFuels.Single().FuelTypeId.Should().NotBe("okko-95-old");
    }
}
