using FuelFlow.Features.Pricing;
using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Pricing;

public sealed class ImportOkkoPumpPricesTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public ImportOkkoPumpPricesTests()
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

    private ImportOkkoPumpPricesCommandHandler Handler() => new(_context, new BrandSlugResolver(_context), _events, new PriceHistoryRecorder(_context));

    /// <summary>The production seed catalog: five OKKO fuel types with their pump/cost/margin knobs.</summary>
    private async Task SeedCatalogAsync()
    {
        _context.Brands.AddRange(
            new Brand { Id = BrandIds.Okko, Slug = "okko", Name = "okko", Color = "#000000", LogoText = "okko" },
            new Brand { Id = BrandIds.Wog, Slug = "wog", Name = "wog", Color = "#000000", LogoText = "wog" });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = ToUuid("okko-95"), Name = "A-95", BrandId = BrandIds.Okko, BasePrice = 54, DiscountPrice = 51 },
            new FuelTypeEntity { Id = ToUuid("okko-dp"), Name = "ДП ЄВРО", BrandId = BrandIds.Okko, BasePrice = 55, DiscountPrice = 52 },
            new FuelTypeEntity { Id = ToUuid("okko-gas"), Name = "ГАЗ", BrandId = BrandIds.Okko, BasePrice = 29, DiscountPrice = 27 },
            new FuelTypeEntity { Id = ToUuid("okko-p95"), Name = "Pulls 95", BrandId = BrandIds.Okko, BasePrice = 60, DiscountPrice = 56 },
            new FuelTypeEntity { Id = ToUuid("okko-pulls-dp"), Name = "ДП PULLS", BrandId = BrandIds.Okko, BasePrice = 58, DiscountPrice = 55 });

        // A competitor fuel the import must not touch.
        _context.FuelTypes.Add(new FuelTypeEntity
            { Id = ToUuid("wog-95"), Name = "A-95 Mustang", BrandId = BrandIds.Wog, BasePrice = 56, DiscountPrice = 53 });

        AddPackages("okko-95", "A-95", 10, 54, 5, 56);
        AddPackages("okko-dp", "ДП ЄВРО", 10, 55, 5, 57);
        AddPackages("okko-gas", "ГАЗ", 10, 29, 4, 31);
        AddPackages("okko-p95", "Pulls 95", 10, 60, 6, 62);
        AddPackages("okko-pulls-dp", "ДП PULLS", 10, 58, 5, 60);
        AddPackages("wog-95", "A-95 Mustang", 10, 56, 5, 58);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private void AddPackages(string fuelTypeId, string fuelName, decimal liters, decimal cost, decimal profit, decimal pump)
    {
        foreach (var nominal in new[] { 10m, 20m, 50m })
        {
            _context.SeedPackage(
                id: $"{fuelTypeId}-{nominal}",
                BrandId: fuelTypeId.StartsWith("okko") ? BrandIds.Okko : BrandIds.Wog,
                fuelTypeId: fuelTypeId,
                liters: nominal,
                // The price these knobs produce, not cost + profit: for WOG the pump ceiling binds, and
                // stating the unbound number here would pin every nominal as an override - which is
                // exactly the drift this suite exists to rule out.
                price: Math.Round(
                    FuelPricing.PackagePrice(cost, profit, pump, nominal),
                    2, MidpointRounding.AwayFromZero),
                supplierPricePerLiter: cost,
                marginUahPerLiter: profit,
                marginPercent: 10m,
                pumpPricePerLiter: pump
);
        }
    }

    /// <summary>The scraper's live output, trimmed to the rows the catalog can match.</summary>
    private const string ScrapedSheet =
        "fuelCode,pricePerLiter\r\n" +
        "Pulls 100,102.90\r\n" +   // jet fuel — no counterpart in the catalog
        "Pulls Diesel,102.90\r\n" +
        "DP,99.90\r\n" +
        "Pulls 95,95.90\r\n" +
        "A-95,92.90\r\n" +
        "SPBT,46.90\r\n" +
        "AdBlue,59.90\r\n";       // additive — not a fuel grade

    private static ImportOkkoPumpPricesCommand Import(string content, bool dryRun = false) =>
        new(content, dryRun);

    [Fact]
    public async Task Import_ShouldMatchEverySiteFuelCode_ToTheRightCatalogFuel()
    {
        await SeedCatalogAsync();

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.FuelsMatched.Should().Be(5);
           result.Applied.Select(a => a.FuelTypeId).Should().BeEquivalentTo(
               [ToUuid("okko-95"), ToUuid("okko-dp"), ToUuid("okko-gas"), ToUuid("okko-p95"), ToUuid("okko-pulls-dp")]);

        // The site code must land on the correct fuel, not merely onto "some" fuel.
        result.Applied.Single(a => a.SiteFuelCode == "A-95").FuelTypeId.Should().Be(ToUuid("okko-95"));
        result.Applied.Single(a => a.SiteFuelCode == "DP").FuelTypeId.Should().Be(ToUuid("okko-dp"));
        result.Applied.Single(a => a.SiteFuelCode == "SPBT").FuelTypeId.Should().Be(ToUuid("okko-gas"));
        result.Applied.Single(a => a.SiteFuelCode == "Pulls 95").FuelTypeId.Should().Be(ToUuid("okko-p95"));
        result.Applied.Single(a => a.SiteFuelCode == "Pulls Diesel").FuelTypeId.Should().Be(ToUuid("okko-pulls-dp"));
    }

    [Fact]
    public async Task Import_ShouldWritePump_AndRepricePackages()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
// The per-litre figures now live on the fuel, once, and the volumes carry only the frozen package
        // total - so the import is asserted at that grain. Asserting it per package is what let the
        // copies drift apart unnoticed.
        _context.ChangeTracker.Clear();

        var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-95"));

    var a95 = await _context.FuelNominals
        .Where(v => v.FuelTypeId == ToUuid("okko-95"))
        .OrderBy(v => v.Liters)
        .ToListAsync();
        // cost 54 + profit 5 = 59, under the 92.90 - 1 = 91.90 ceiling.
        fuel.FinalPerLiter().Should().Be(59m);
        foreach (var volume in a95)
        {
            // No stored total and no override: the package price is the rate × litres, and it is
            // computed by the same function the storefront and checkout use.
            var price = FuelPricing.PackagePrice(
                fuel.SupplierPricePerLiter,
                await MarginFor(fuel),
                fuel.PumpPricePerLiter,
                volume.Liters);
            price.Should().Be(Math.Round(59m * volume.Liters, 2, MidpointRounding.AwayFromZero));
            // The struck "before" price is the pump price × litres, recomputed from the offer rather than
            // stored - it was never authoritative, and a stale stored copy could leak a supplier cost.
            FuelPricing.OriginalPackagePrice(fuel.PumpPricePerLiter, volume.Liters, price)
                .Should().Be(Math.Round(92.90m * volume.Liters, 2, MidpointRounding.AwayFromZero));
        }

        fuel.PriceUpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_ShouldHoldSupplierCost_AndOnlyMoveTheCeiling()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var (_, fuel) = await ReadAsync("okko-95-20");

        // A pump change is not a cost event: cost and margin must be untouched. These live on the offer
        // now, so one read covers every volume of the fuel - there is no per-package copy left to miss.
        fuel.SupplierPricePerLiter.Should().Be(54m);
        (await MarginFor(fuel)).Should().Be(5m, "the margin lives on the fuel; the offer's own copy is never written");
    }

    [Fact]
    public async Task Import_ShouldUpdateFuelTypeHeadline_ToPumpAndCustomerPrice()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var gas = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-gas"));
        // The column price is written - it is the reference. The price the customer pays is
        // computed from the knobs, so it is not written at all.
        gas.PumpPricePerLiter.Should().Be(46.90m);
        gas.FinalPerLiter().Should().Be(33m);   // cost 29 + profit 4, under the column price
    }

    [Fact]
    public async Task Import_ShouldApplyAPumpBelowOurCost_BecauseTheColumnPriceIsAReference()
    {
        // The column price is what the pump charges; our price is the cost plus the margin. A pump
        // under what we pay is therefore written as it stands - the import neither refuses it nor
        // needs an opt-in to apply it. The guard that can lose us money is at checkout, where the
        // price the customer is actually charged is compared against our cost.
        await SeedCatalogAsync();
        var a95 = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-95"));
        a95.SupplierPricePerLiter.Should().NotBeNull();
        a95.SupplierPricePerLiter!.Value.Should().BeGreaterThan(50m,
            "the pump the sheet publishes is under what we pay - that is what makes this the below-cost case");

        var result = await Handler().HandleAsync(
            Import("fuelCode,pricePerLiter\r\nA-95,50.00\r\n"), Guid.Empty, "tester");

        result.FuelsChanged.Should().Be(1);
        _context.ChangeTracker.Clear();
        (await ReadAsync("okko-95-20")).Fuel.PumpPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task Import_DryRun_ShouldNotMutateAnything()
    {
        await SeedCatalogAsync();
        var (_, beforeFuel) = await ReadAsync("okko-95-20");

        var result = await Handler().HandleAsync(Import(ScrapedSheet, dryRun: true), Guid.Empty, "tester");

        result.DryRun.Should().BeTrue();
        result.FuelsMatched.Should().Be(5);
        result.FuelsChanged.Should().Be(5);
        _context.ChangeTracker.Clear();
        var (afterVolume, afterFuel) = await ReadAsync("okko-95-20");
        afterFuel.PumpPricePerLiter.Should().Be(beforeFuel.PumpPricePerLiter);
        _context.AuditEventOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ShouldNotTouchOtherBrands()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

var (wogVolume, wogFuel) = await ReadAsync("wog-95-20");
        wogFuel.PumpPricePerLiter.Should().Be(58m);
        // Untouched: this brand's rate did not move, so its nominals still work out from the fuel's
        // own rate - 56 cost + 5 margin = 61, capped by the ceiling at pump 58 less the guaranteed
        // 1 UAH discount, which is 57 and therefore what the customer pays. Derived through the
        // formula rather than from a stored total, which is all there has ever been.
        wogFuel.FinalPerLiter().Should().Be(61m);
    }

    [Fact]
    public async Task Import_ShouldReportSiteFuels_ThatHaveNoCatalogCounterpart()
    {
        await SeedCatalogAsync();

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        // Jet fuel and the AdBlue additive are published but out of scope — reported, not guessed at.
        result.UnmatchedSiteFuels.Should().BeEquivalentTo(["Pulls 100", "AdBlue"]);
    }

    [Fact]
    public async Task Import_ShouldReportCatalogFuels_LeftWithoutAPrice()
    {
        await SeedCatalogAsync();

        var result = await Handler().HandleAsync(Import("fuelCode,pricePerLiter\r\nA-95,92.90\r\n"), Guid.Empty, "tester");

        result.FuelsMatched.Should().Be(1);
        result.OkkoFuelsWithoutPrice.Should().BeEquivalentTo(
            ["ДП ЄВРО", "ГАЗ", "Pulls 95", "ДП PULLS"]);
    }

    [Fact]
    public async Task Import_MatchesByTheBrandCode_WhereTheCatalogueCarriesOne()
    {
        // The lookup the whole column exists for: the sheet publishes a code, the catalogue carries the
        // same code, and matching them is equality rather than an interpretation of two names.
        await SeedCatalogAsync();
        await SetCodeAsync("okko-95", "A-95");

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.Applied.Should().Contain(a => a.FuelTypeId == ToUuid("okko-95") && a.SiteFuelCode == "A-95");

        _context.ChangeTracker.Clear();
        var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-95"));
        fuel.PumpPricePerLiter.Should().Be(92.90m);
    }

    [Fact]
    public async Task ACodeTheSheetDoesNotCarry_LeavesTheFuelUnpriced_AndDoesNotFallBackToTheName()
    {
        // The sharp edge. "ДП ЄВРО" and a second diesel both classify as Diesel, so before the code
        // existed both would have been handed the sheet's diesel price; with a code that the sheet does
        // not publish, the fuel simply has no price today. Guessing would give one brand's grade
        // another's ceiling, and which one won was decided by id order.
        await SeedCatalogAsync();
        await SetCodeAsync("okko-95", "A-95 Premium");   // a code OKKO does not publish

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.Applied.Should().NotContain(a => a.FuelTypeId == ToUuid("okko-95"),
            "the name still classifies as A95, and that is exactly the guess the code replaces");
        result.OkkoFuelsWithoutPrice.Should().Contain("A-95");
    }

    [Fact]
    public async Task TwoFuelsOfOneCategory_AreSeparatedByTheirCode()
    {
        // The case a category cannot express: two A-95s at one brand, one of them the one the sheet is
        // pricing. Both names classify identically, so without a code both receive the same price.
        await SeedCatalogAsync();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("okko-95-euro"),
            Name = "A-95 EURO",
            BrandId = BrandIds.Okko,
            BasePrice = 56,
            DiscountPrice = 53,
            BrandFuelCode = "A-95"
        });
        AddPackages("okko-95-euro", "A-95 EURO", 10, 56, 5, 58);
        await _context.SaveChangesAsync();
        await SetCodeAsync("okko-95", "A-95 Extra");     // a second grade the sheet does not publish

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.Applied.Should().ContainSingle(a => a.SiteFuelCode == "A-95").Which.FuelTypeId
            .Should().Be(ToUuid("okko-95-euro"), "the code says which A-95 the sheet is pricing");
        result.OkkoFuelsWithoutPrice.Should().Contain("A-95");
    }

    [Fact]
    public async Task ACode_IsComparedTrimmed()
    {
        // Sheets carry trailing spaces; the stored code is trimmed by the one normalisation rule. A
        // mismatch here would silently stop pricing a fuel that is on the site.
        await SeedCatalogAsync();
        await SetCodeAsync("okko-95", " A-95 ");

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.Applied.Should().Contain(a => a.FuelTypeId == ToUuid("okko-95"));
    }

    private async Task SetCodeAsync(string fuelTypeId, string code)
    {
        var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid(fuelTypeId));
        fuel.BrandFuelCode = code;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Import_ShouldAppendTheNewPrice_ToThePriceHistory()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        // This path runs unattended over the whole catalogue every quarter hour, which is exactly why
        // it has to be the one that cannot forget the history: nobody is watching it not to.
        var prices = await _context.FuelPriceHistory.ToListAsync();

        prices.Should().HaveCount(5, "one opening row per fuel the import repriced");
        prices.Should().OnlyContain(p => p.Reason == PriceHistoryReasons.PumpImport);
        prices.Should().OnlyContain(p => p.ChangedByUserId == null, "no person typed these");
prices.Should().OnlyContain(p => p.BrandId == BrandIds.Okko && p.DataKind == DataKinds.Real);

        // Each row holds what its fuel now holds - the invariant the history rests on, checked here at
        // the path that writes most rows rather than only in the schema-level suite.
        foreach (var price in prices)
        {
            var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == price.FuelTypeId);
            price.PumpPricePerLiter.Should().Be(fuel.PumpPricePerLiter);
            // The recorded figure is the derived one: the history stores what the customer paid, not the
            // inputs it came from, which is the whole reason the column exists.
            price.FinalPricePerLiter.Should().Be(fuel.FinalPerLiter());
            price.SupplierPricePerLiter.Should().Be(fuel.SupplierPricePerLiter);
        }
    }

    [Fact]
    public async Task Import_ShouldAppendNothing_WhenTheSheetMovesNoPrice()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();
        var before = await _context.FuelPriceHistory.CountAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        // Twenty-five rows a quarter hour is noise that buries the changes the history exists to show.
        (await _context.FuelPriceHistory.CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Import_ShouldRecordAnAuditEvent_PerChangedFuel()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var events = await _context.AuditEventOutbox
            .Where(e => e.EventType == "PumpPriceImported")
            .OrderBy(e => e.AggregateId)
            .ToListAsync();

        events.Should().HaveCount(5);
        events.Should().OnlyContain(e => e.ScopeId == "okko");
           events.Select(e => e.AggregateId).Should().BeEquivalentTo(
               [ToUuid("okko-95").ToString(), ToUuid("okko-dp").ToString(), ToUuid("okko-gas").ToString(),
                ToUuid("okko-p95").ToString(), ToUuid("okko-pulls-dp").ToString()]);
        events.Should().OnlyContain(e => e.ChangedByUserName == "tester");
        events.Should().OnlyContain(e => e.Summary!.Contains("imported from site"));
    }

    [Fact]
    public async Task Import_ShouldRecordNoEvents_AndNoWrites_WhenNothingChanged()
    {
        await SeedCatalogAsync();

        // First import moves every fuel onto the site's prices.
        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();
        _context.AuditEventOutbox.RemoveRange(_context.AuditEventOutbox);
        await _context.SaveChangesAsync();

        // Re-importing the same sheet is a no-op: no churn in the audit trail, no writes.
        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.FuelsMatched.Should().Be(5);
        result.FuelsChanged.Should().Be(0);
        _context.ChangeTracker.Clear();
        _context.AuditEventOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ShouldStampTheActingUser_OnRepricedPackages()
    {
        await SeedCatalogAsync();
        var actor = Guid.NewGuid();

        await Handler().HandleAsync(Import(ScrapedSheet), actor, "tester");
        _context.ChangeTracker.Clear();

        var (_, fuel) = await ReadAsync("okko-95-20");
        fuel.PriceUpdatedByUserId.Should().Be(actor);
    }

    [Fact]
    public async Task Import_ReportsZeroPackages_ForAMatchedFuelWithNoSizes()
    {
        await SeedCatalogAsync();

        // No sizes at all for a matched fuel. There is nothing to reprice per size any more - the price
        // is the fuel's - so the import still writes the pump, still records the history, and still
        // reports the fuel rather than throwing.
        _context.FuelNominals.RemoveRange(_context.FuelNominals.Where(v => v.FuelTypeId == ToUuid("okko-gas")));
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.Applied.Single(a => a.FuelTypeId == ToUuid("okko-gas")).PackagesRepriced.Should().Be(0);
        result.PackagesRepriced.Should().Be(12);

        var fuel = await _context.FuelTypes.AsNoTracking().FirstAsync(f => f.Id == ToUuid("okko-gas"));
        fuel.PumpPricePerLiter.Should().NotBeNull("a fuel with no sizes is still a fuel on the shelf");
    }

    [Fact]
    public async Task Import_ShouldRejectASheet_WithNoRows_AndReportTheError()
    {
        await SeedCatalogAsync();

        var result = await Handler().HandleAsync(Import("fuelCode,pricePerLiter\r\n"), Guid.Empty, "tester");

        result.FuelsMatched.Should().Be(0);
        result.OkkoFuelsWithoutPrice.Should().HaveCount(5);
    }

    [Theory]
    // A zero pump price would become the ceiling for every package and drive the customer price to
    // zero — the importer must refuse the row rather than write it.
    [InlineData("A-95,0", "greater than 0")]
    [InlineData("A-95,-5.00", "greater than 0")]
    [InlineData("A-95,abc", "Invalid pricePerLiter")]
    [InlineData("A-95,", "pricePerLiter is required")]
    [InlineData(",92.90", "fuelCode is required")]
    public void Parse_ShouldRejectBadRows_PerLine(string row, string expected)
    {
        var result = OkkoPriceSheetParser.Parse($"fuelCode,pricePerLiter\r\n{row}\r\n");

        result.Rows.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ShouldRejectAFile_WithTheWrongColumns()
    {
        var result = OkkoPriceSheetParser.Parse("name,price\r\nA-95,92.90\r\n");

        result.Rows.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Message.Contains("fuelCode", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ShouldParseInvariantDecimalPoints_RegardlessOfOperatorLocale()
    {
        // The scraper writes "92.90" with a dot; a comma decimal separator must not be inferred.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("uk-UA");
            var result = OkkoPriceSheetParser.Parse("fuelCode,pricePerLiter\r\nA-95,92.90\r\n");
            result.Rows.Should().ContainSingle();
            result.Rows[0].PricePerLiter.Should().Be(92.90m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Import_ShouldRecordTheSiteCodeInTheAuditPayload()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import("fuelCode,pricePerLiter\r\nPulls Diesel,102.90\r\n"), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var ev = await _context.AuditEventOutbox.AsNoTracking().FirstAsync(e => e.EventType == "PumpPriceImported");
        using var doc = JsonDocument.Parse(ev.NewValue!);
        doc.RootElement.GetProperty("SiteFuelCode").GetString().Should().Be("Pulls Diesel");
        doc.RootElement.GetProperty("PumpPricePerLiter").GetDecimal().Should().Be(102.90m);
    }
    /// <summary>
    /// Reads a package the way the application does: the volume, plus the fuel that carries the
    /// per-litre figures. Tests that assert on a single package almost always want both halves.
    /// </summary>
    private async Task<(FuelNominal Volume, FuelTypeEntity Fuel)> ReadAsync(string fuelTypeIdAndLiters)
    {
        // "okko-95-20" is a fuel id and a size, joined by the last dash. The size used to be the tail
        // of a volume row's id; a nominal has no id at all, so the fixture names the pair it means.
        var split = fuelTypeIdAndLiters.LastIndexOf('-');
        var fuelTypeId = fuelTypeIdAndLiters[..split];
        var liters = decimal.Parse(
            fuelTypeIdAndLiters[(split + 1)..],
            System.Globalization.CultureInfo.InvariantCulture);

        var volume = await _context.FuelNominals.AsNoTracking()
            .FirstAsync(v => v.FuelTypeId == ToUuid(fuelTypeId) && v.Liters == liters);
        var fuel = await _context.FuelTypes.AsNoTracking().FirstAsync(f => f.Id == ToUuid(fuelTypeId));
        return (volume, fuel);
    }

    /// <summary>
    /// The margin a fuel sells at, read from the fuel - the one place it lives.
    /// </summary>
    private async Task<decimal?> MarginFor(FuelTypeEntity fuel)
        => await _context.FuelTypes
            .Where(f => f.Id == fuel.Id)
            .Select(f => f.MarginUahPerLiter)
            .FirstOrDefaultAsync();
}
