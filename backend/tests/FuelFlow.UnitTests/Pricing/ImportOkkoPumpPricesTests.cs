using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

public sealed class ImportOkkoPumpPricesTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ProviderEventService _events;

    public ImportOkkoPumpPricesTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _events = new ProviderEventService(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private ImportOkkoPumpPricesCommandHandler Handler() => new(_context, _events);

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
            _context.FuelPackages.Add(new FuelPackage
            {
                Id = $"{fuelTypeId}-{nominal}",
                StationId = fuelTypeId.StartsWith("okko") ? "okko" : "wog",
                FuelTypeId = fuelTypeId,
                FuelName = fuelName,
                Liters = nominal,
                SupplierPricePerLiter = cost,
                MarginUahPerLiter = profit,
                MarginPercent = 10m,
                PumpPricePerLiter = pump,
                MinDiscountPerLiter = 1m,
                FinalPricePerLiter = cost + profit,
                Price = Math.Round((cost + profit) * nominal, 2, MidpointRounding.AwayFromZero),
                OriginalPrice = Math.Round(pump * nominal, 2, MidpointRounding.AwayFromZero),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
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
        _context.ChangeTracker.Clear();

        var a95 = await _context.FuelPackages
            .Where(p => p.FuelTypeId == "okko-95")
            .OrderBy(p => p.Liters)
            .ToListAsync();

        a95.Should().HaveCount(3);
        foreach (var pkg in a95)
        {
            pkg.PumpPricePerLiter.Should().Be(92.90m);
            // cost 54 + profit 5 = 59, under the 92.90 - 1 = 91.90 ceiling.
            pkg.FinalPricePerLiter.Should().Be(59m);
            pkg.Price.Should().Be(Math.Round(59m * pkg.Liters, 2, MidpointRounding.AwayFromZero));
            // The struck "до" price is the pump price × liters.
            pkg.OriginalPrice.Should().Be(Math.Round(92.90m * pkg.Liters, 2, MidpointRounding.AwayFromZero));
        }

        a95[0].PriceUpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Import_ShouldHoldSupplierCost_AndOnlyMoveTheCeiling()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var pkg = await _context.FuelPackages.FirstAsync(p => p.Id == "okko-95-20");

        // A pump change is not a cost event: cost and margin must be untouched.
        pkg.SupplierPricePerLiter.Should().Be(54m);
        pkg.MarginUahPerLiter.Should().Be(5m);
        pkg.MinDiscountPerLiter.Should().Be(1m);
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

        // A-95 at 50 UAH/L: cost 54 + profit 5 = 59, so the ceiling (50 - 1 = 49) must win — the
        // customer's price follows the pump down rather than staying at the cost-plus price.
        await Handler().HandleAsync(Import("fuelCode,pricePerLiter\r\nA-95,50.00\r\n"), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var pkg = await _context.FuelPackages.FirstAsync(p => p.Id == "okko-95-20");
        pkg.FinalPricePerLiter.Should().Be(49m);
        pkg.Price.Should().Be(980m);
        pkg.OriginalPrice.Should().Be(1000m); // the struck "до" price is pump × liters
    }

    [Fact]
    public async Task Import_DryRun_ShouldNotMutateAnything()
    {
        await SeedCatalogAsync();
        var before = await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-95-20");

        var result = await Handler().HandleAsync(Import(ScrapedSheet, dryRun: true), Guid.Empty, "tester");

        result.DryRun.Should().BeTrue();
        result.FuelsMatched.Should().Be(5);
        result.FuelsChanged.Should().Be(5);
        _context.ChangeTracker.Clear();
        var after = await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-95-20");
        after.PumpPricePerLiter.Should().Be(before.PumpPricePerLiter);
        after.Price.Should().Be(before.Price);
        _context.ProviderEventOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ShouldNotTouchOtherBrands()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var wog = await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "wog-95-20");
        wog.PumpPricePerLiter.Should().Be(58m);
        wog.Price.Should().Be(1220m); // untouched: (56 + 5) per liter × 20
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
    public async Task Import_ShouldRecordAnAuditEvent_PerChangedFuel()
    {
        await SeedCatalogAsync();

        await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");
        _context.ChangeTracker.Clear();

        var events = await _context.ProviderEventOutbox
            .Where(e => e.EventType == "PumpPriceImported")
            .OrderBy(e => e.AggregateId)
            .ToListAsync();

        events.Should().HaveCount(5);
        events.Should().OnlyContain(e => e.ProviderId == "okko");
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
        _context.ProviderEventOutbox.RemoveRange(_context.ProviderEventOutbox);
        await _context.SaveChangesAsync();

        // Re-importing the same sheet is a no-op: no churn in the audit trail, no writes.
        var result = await Handler().HandleAsync(Import(ScrapedSheet), Guid.Empty, "tester");

        result.FuelsMatched.Should().Be(5);
        result.FuelsChanged.Should().Be(0);
        _context.ChangeTracker.Clear();
        _context.ProviderEventOutbox.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ShouldStampTheActingUser_OnRepricedPackages()
    {
        await SeedCatalogAsync();
        var actor = Guid.NewGuid();

        await Handler().HandleAsync(Import(ScrapedSheet), actor, "tester");
        _context.ChangeTracker.Clear();

        var pkg = await _context.FuelPackages.AsNoTracking().FirstAsync(p => p.Id == "okko-95-20");
        pkg.PriceUpdatedByUserId.Should().Be(actor);
    }

    [Fact]
    public async Task Import_ShouldReuseTheFirstPackageAsTheCostRepresentative()
    {
        await SeedCatalogAsync();

        // No packages at all for a matched fuel: the price still applies to the fuel-type headline
        // and the import reports zero packages rather than throwing.
        _context.FuelPackages.RemoveRange(_context.FuelPackages.Where(p => p.FuelTypeId == "okko-gas"));
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

        var ev = await _context.ProviderEventOutbox.AsNoTracking().FirstAsync(e => e.EventType == "PumpPriceImported");
        using var doc = JsonDocument.Parse(ev.NewValue!);
        doc.RootElement.GetProperty("SiteFuelCode").GetString().Should().Be("Pulls Diesel");
        doc.RootElement.GetProperty("PumpPricePerLiter").GetDecimal().Should().Be(102.90m);
    }
}