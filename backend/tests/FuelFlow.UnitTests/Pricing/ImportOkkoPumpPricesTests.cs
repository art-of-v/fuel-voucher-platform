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

    private ImportOkkoPumpPricesCommandHandler Handler() => new(_context, _events, new OfferPriceRecorder(_context));

    /// <summary>The production seed catalog: five OKKO fuel types with their pump/cost/margin knobs.</summary>
    private async Task SeedCatalogAsync()
    {
        _context.Stations.AddRange(
            new Station { Id = "okko", Name = "OKKO", Color = "#000000", LogoText = "OKKO" },
            new Station { Id = "wog", Name = "WOG", Color = "#000000", LogoText = "WOG" });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 54, DiscountPrice = 51 },
            new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52 },
            new FuelTypeEntity { Id = "okko-gas", Name = "ГАЗ", StationId = "okko", BasePrice = 29, DiscountPrice = 27 },
            new FuelTypeEntity { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BasePrice = 60, DiscountPrice = 56 },
            new FuelTypeEntity { Id = "okko-pulls-dp", Name = "ДП PULLS", StationId = "okko", BasePrice = 58, DiscountPrice = 55 });

        // A competitor fuel the import must not touch.
        _context.FuelTypes.Add(new FuelTypeEntity
            { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53 });

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
                stationId: fuelTypeId.StartsWith("okko") ? "okko" : "wog",
                fuelTypeId: fuelTypeId,
                liters: nominal,
                price: Math.Round((cost + profit) * nominal, 2, MidpointRounding.AwayFromZero),
                supplierPricePerLiter: cost,
                marginUahPerLiter: profit,
                marginPercent: 10m,
                pumpPricePerLiter: pump,
                minDiscountPerLiter: 1m);
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
            ["okko-95", "okko-dp", "okko-gas", "okko-p95", "okko-pulls-dp"]);

        // The site code must land on the correct fuel, not merely onto "some" fuel.
        result.Applied.Single(a => a.SiteFuelCode == "A-95").FuelTypeId.Should().Be("okko-95");
        result.Applied.Single(a => a.SiteFuelCode == "DP").FuelTypeId.Should().Be("okko-dp");
        result.Applied.Single(a => a.SiteFuelCode == "SPBT").FuelTypeId.Should().Be("okko-gas");
        result.Applied.Single(a => a.SiteFuelCode == "Pulls 95").FuelTypeId.Should().Be("okko-p95");
        result.Applied.Single(a => a.SiteFuelCode == "Pulls Diesel").FuelTypeId.Should().Be("okko-pulls-dp");
    }

    [Fact]
    public async Task Import_ShouldWritePump_AndRepricePackages()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
// The per-litre figures now live on the offer, once, and the volumes carry only the frozen package
        // total - so the import is asserted at that grain. Asserting it per package is what let the
        // copies drift apart unnoticed.
        _context.ChangeTracker.Clear();

        var offer = await _context.FuelOffers
            .FirstAsync(o => o.FuelTypeId == "okko-95" && o.DataKind == DataKinds.Real);

        var a95 = await _context.FuelOfferVolumes
            .Where(v => v.OfferId == offer.Id)
            .OrderBy(v => v.Liters)
            .ToListAsync();

        a95.Should().HaveCount(3);
        offer.PumpPricePerLiter.Should().Be(92.90m);
        // cost 54 + profit 5 = 59, under the 92.90 - 1 = 91.90 ceiling.
        offer.ComputedFinalPricePerLiter.Should().Be(59m);
        foreach (var volume in a95)
        {
            volume.PackagePrice.Should().Be(Math.Round(59m * volume.Liters, 2, MidpointRounding.AwayFromZero));
            // The struck "??" price is the pump price x litres, recomputed from the offer rather than
            // stored - it was never authoritative, and a stale stored copy could leak a supplier cost.
            FuelPricing.OriginalPackagePrice(offer.PumpPricePerLiter, volume.Liters, volume.PackagePrice)
                .Should().Be(Math.Round(92.90m * volume.Liters, 2, MidpointRounding.AwayFromZero));
        }

        offer.PriceUpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_ShouldHoldSupplierCost_AndOnlyMoveTheCeiling()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var (_, offer) = await ReadAsync("okko-95-20");

        // A pump change is not a cost event: cost and margin must be untouched. These live on the offer
        // now, so one read covers every volume of the fuel - there is no per-package copy left to miss.
        offer.SupplierPricePerLiter.Should().Be(54m);
        offer.MarginUahPerLiter.Should().Be(5m);
        offer.MinDiscountPerLiter.Should().Be(1m);
    }

    [Fact]
    public async Task Import_ShouldUpdateFuelTypeHeadline_ToPumpAndCustomerPrice()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var gas = await _context.FuelTypes.FirstAsync(f => f.Id == "okko-gas");
        gas.BasePrice.Should().Be(46.90m);   // колонка
        gas.DiscountPrice.Should().Be(33m);   // cost 29 + profit 4, under 46.90 - 1
    }

    [Fact]
    public async Task Import_ShouldLeaveCeilingBinding_WhenPumpDropsBelowCostPlusProfit()
    {
        await SeedCatalogAsync();

        // A-95 at 57 UAH/L: cost 54 + profit 5 = 59, so the ceiling (57 - 1 = 56) must win — the
        // customer's price follows the pump down rather than staying at the cost-plus price. Still
        // above the 54 cost, so this is an ordinary discount and not a below-cost sale.
        await Handler().HandleAsync(Import("fuelCode,pricePerLiter\r\nA-95,57.00\r\n"), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

var (volume, offer) = await ReadAsync("okko-95-20");
        offer.PumpPricePerLiter.Should().Be(57m);
        offer.ComputedFinalPricePerLiter.Should().Be(56m);
        volume.PackagePrice.Should().Be(1120m);
        // The struck "??" price is pump x litres, recomputed from the offer rather than read from a
        // stored column - which is what stops a stale copy from leaking a supplier cost.
        FuelPricing.OriginalPackagePrice(offer.PumpPricePerLiter, volume.Liters, volume.PackagePrice)
            .Should().Be(1140m);
    }

    [Fact]
    public async Task Import_ShouldRefuseAPumpThatWouldSellBelowCost()
    {
        await SeedCatalogAsync();

        // A-95 at 50: the ceiling (50 - 1 = 49) sits under the 54 cost. Applying it would price every
        // litre under cost, and checkout then refuses to sell the fuel at all — so the import leaves
        // the catalog alone, exactly as the operator panel refuses the same typed-in value.
        var result = await Handler().HandleAsync(
            Import("fuelCode,pricePerLiter\r\nA-95,50.00\r\n"), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        result.BelowCostSkipped.Should().ContainSingle().Which.Should().Be("A-95");
        result.FuelsChanged.Should().Be(0);

        var (_, offer) = await ReadAsync("okko-95-20");
        offer.PumpPricePerLiter.Should().Be(56m); // unchanged from the seed
    }

    [Fact]
    public async Task Import_ShouldApplyABelowCostPump_WhenTheFuelIsOptedIn()
    {
        await SeedCatalogAsync();
        var a95 = await _context.FuelTypes.FirstAsync(f => f.Id == "okko-95");
        a95.AllowBelowCost = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await Handler().HandleAsync(
            Import("fuelCode,pricePerLiter\r\nA-95,50.00\r\n"), Guid.Empty, "tester");

        result.BelowCostSkipped.Should().BeEmpty();
        result.FuelsChanged.Should().Be(1);
        _context.ChangeTracker.Clear();
        (await ReadAsync("okko-95-20")).Offer.PumpPricePerLiter.Should().Be(50m);
    }

    [Fact]
    public async Task Import_DryRun_ShouldNotMutateAnything()
    {
        await SeedCatalogAsync();
        var (_, beforeOffer) = await ReadAsync("okko-95-20");
        var beforePrice = (await _context.FuelOfferVolumes.AsNoTracking().FirstAsync(v => v.Id == "okko-95-20")).PackagePrice;

        var result = await Handler().HandleAsync(Import(ScrapedSheet, dryRun: true), Guid.Empty, "tester");

        result.DryRun.Should().BeTrue();
        result.FuelsMatched.Should().Be(5);
        result.FuelsChanged.Should().Be(5);
        _context.ChangeTracker.Clear();
        var (afterVolume, afterOffer) = await ReadAsync("okko-95-20");
        afterOffer.PumpPricePerLiter.Should().Be(beforeOffer.PumpPricePerLiter);
        afterVolume.PackagePrice.Should().Be(beforePrice);
        _context.AuditEventOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ShouldNotTouchOtherBrands()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

var (wogVolume, wogOffer) = await ReadAsync("wog-95-20");
        wogOffer.PumpPricePerLiter.Should().Be(58m);
        wogVolume.PackagePrice.Should().Be(1220m); // untouched: (56 + 5) per litre x 20
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

        result.Applied.Should().Contain(a => a.FuelTypeId == "okko-95" && a.SiteFuelCode == "A-95");

        _context.ChangeTracker.Clear();
        var offer = await _context.FuelOffers.FirstAsync(o => o.FuelTypeId == "okko-95" && o.DataKind == DataKinds.Real);
        offer.PumpPricePerLiter.Should().Be(92.90m);
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

        result.Applied.Should().NotContain(a => a.FuelTypeId == "okko-95",
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
            Id = "okko-95-euro",
            Name = "A-95 EURO",
            StationId = "okko",
            BasePrice = 56,
            DiscountPrice = 53,
            BrandFuelCode = "A-95"
        });
        AddPackages("okko-95-euro", "A-95 EURO", 10, 56, 5, 58);
        await _context.SaveChangesAsync();
        await SetCodeAsync("okko-95", "A-95 Extra");     // a second grade the sheet does not publish

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.Applied.Should().ContainSingle(a => a.SiteFuelCode == "A-95").Which.FuelTypeId
            .Should().Be("okko-95-euro", "the code says which A-95 the sheet is pricing");
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

        result.Applied.Should().Contain(a => a.FuelTypeId == "okko-95");
    }

    private async Task SetCodeAsync(string fuelTypeId, string code)
    {
        var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == fuelTypeId);
        fuel.BrandFuelCode = code;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Import_ShouldAppendTheNewPrice_ToTheOfferHistory()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        // This path runs unattended over the whole catalogue every quarter hour, which is exactly why
        // it has to be the one that cannot forget the history: nobody is watching it not to.
        var prices = await _context.FuelOfferPrices.ToListAsync();

        prices.Should().HaveCount(5, "one opening row per offer the import repriced");
        prices.Should().OnlyContain(p => p.Reason == OfferPriceReasons.PumpImport);
        prices.Should().OnlyContain(p => p.ChangedByUserId == null, "no person typed these");
        prices.Should().OnlyContain(p => p.StationId == "okko" && p.DataKind == DataKinds.Real);

        // Each row holds what its offer now holds - the invariant the history rests on, checked here at
        // the path that writes most rows rather than only in the schema-level suite.
        foreach (var price in prices)
        {
            var offer = await _context.FuelOffers.FirstAsync(o => o.Id == price.OfferId);
            price.PumpPricePerLiter.Should().Be(offer.PumpPricePerLiter);
            // The recorded figure is the derived one: the history stores what the customer paid, not the
            // inputs it came from, which is the whole reason the column exists.
            price.FinalPricePerLiter.Should().Be(offer.ComputedFinalPricePerLiter);
            price.SupplierPricePerLiter.Should().Be(offer.SupplierPricePerLiter);
        }
    }

    [Fact]
    public async Task Import_ShouldAppendNothing_WhenTheSheetMovesNoPrice()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();
        var before = await _context.FuelOfferPrices.CountAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        // Twenty-five rows a quarter hour is noise that buries the changes the history exists to show.
        (await _context.FuelOfferPrices.CountAsync()).Should().Be(before);
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
            ["okko-95", "okko-dp", "okko-gas", "okko-p95", "okko-pulls-dp"]);
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

        var (_, offer) = await ReadAsync("okko-95-20");
        offer.PriceUpdatedByUserId.Should().Be(actor);
    }

    [Fact]
    public async Task Import_ShouldReuseTheFirstPackageAsTheCostRepresentative()
    {
        await SeedCatalogAsync();

        // No packages at all for a matched fuel: the price still applies to the fuel-type headline
        // and the import reports zero packages rather than throwing.
        //
        // "No packages" now means no VOLUMES, and the offer has to go with them - an offer with zero
        // volumes is still an offer the import would reprice, so leaving it would test the wrong thing.
        // Volumes first: the relation is RESTRICT, so the offer cannot be deleted while they point at it.
        var gasOfferId = await _context.FuelOffers
            .Where(o => o.FuelTypeId == "okko-gas")
            .Select(o => o.Id)
            .FirstAsync();
        _context.FuelOfferVolumes.RemoveRange(_context.FuelOfferVolumes.Where(v => v.OfferId == gasOfferId));
        await _context.SaveChangesAsync();
        _context.FuelOffers.RemoveRange(_context.FuelOffers.Where(o => o.Id == gasOfferId));
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.Applied.Single(a => a.FuelTypeId == "okko-gas").PackagesRepriced.Should().Be(0);
        result.PackagesRepriced.Should().Be(12);
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
    /// Reads a package the way the application does: the volume, plus the offer that carries the
    /// per-litre figures. Tests that assert on a single package almost always want both halves.
    /// </summary>
    private async Task<(FuelOfferVolume Volume, FuelOffer Offer)> ReadAsync(string volumeId)
    {
        var row = await _context.FuelOfferVolumes.AsNoTracking()
            .Where(v => v.Id == volumeId)
            .Select(v => new { Volume = v, Offer = v.Offer })
            .FirstAsync();
        return (row.Volume, row.Offer);
    }
}
