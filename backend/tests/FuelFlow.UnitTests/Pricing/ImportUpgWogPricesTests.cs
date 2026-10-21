using System.Text;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.ImportUpgWogPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// The UPG and WOG price import, against the catalogues those two brands actually publish.
/// </summary>
/// <remarks>
/// The property that matters most is the last one in this file: an unchanged sheet must write
/// nothing. A brand publishes a price list every time anyone loads the page, so an importer that
/// touched a row on every run would rewrite the catalogue continuously and bury the price history
/// under identical versions.
/// </remarks>
public sealed class ImportUpgWogPricesTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public ImportUpgWogPricesTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.Brands.AddRange(
            new Brand { Id = BrandIds.Upg, Slug = "upg", Name = "UPG", Color = "#06b6d4", LogoText = "UPG" },
            new Brand { Id = BrandIds.Wog, Slug = "wog", Name = "wog", Color = "#000000", LogoText = "wog" });
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private ImportUpgWogPricesCommandHandler Handler() => new(_context, new PriceHistoryRecorder(_context), new BrandSlugResolver(_context));

    private static string Sheet(params (string Code, decimal Price)[] rows)
    {
        var sb = new StringBuilder("fuelCode,pricePerLiter\r\n");
        foreach (var (code, price) in rows)
        {
            // RFC 4180, as scripts/lib/price-csv.mjs writes it - the WOG labels contain commas and
            // spaces, so a naive split would tear those rows in half.
            var field = code.Contains(',') ? $"\"{code.Replace("\"", "\"\"")}\"" : code;
            sb.Append(field).Append(',').Append(price.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
        }
        return sb.ToString();
    }

    private FuelTypeEntity SeedFuel(string id, Guid brandId, string name, string? code, decimal? cost, decimal? profit, decimal pump)
    {
        var fuel = new FuelTypeEntity
        {
            Id = ToUuid(id),
            BrandId = brandId,
            Name = name,
            BrandFuelCode = code,
            BasePrice = pump,
            // A fuel with no cost has no customer price either - which is the state the catalogue's
            // own fuels are in, and the state the import has to leave them in.
            DiscountPrice = cost is null ? null : pump - 1m,
            MarginUahPerLiter = profit,
            SupplierPricePerLiter = cost,
            PumpPricePerLiter = pump,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.FuelTypes.Add(fuel);

        // The price is the fuel's, so there is no offer row to add here.
        foreach (var liters in new[] { 10, 20, 50 })
        {
            _context.FuelNominals.Add(new FuelNominal
            {
        FuelTypeId = ToUuid(id),
                Liters = liters,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        return fuel;
    }

    // The real UPG sheet, as published: slugs, two distinct 95s and two distinct diesels.
    private static readonly (string Code, decimal Price)[] UpgSheet =
    [
        ("upg100", 99.90m), ("upg95", 91.90m), ("A-95", 88.90m),
        ("EURO DIESEL", 101.49m), ("upgDIESEL", 103.50m), ("AdBlue", 49.90m), ("Газ", 50.50m)
    ];

    // The real WOG sheet: readable labels, which is the whole difference from UPG.
    private static readonly (string Code, decimal Price)[] WogSheet =
    [
        ("ДП Євро5", 103.50m), ("ДП Mustang+", 106.50m), ("95 Євро5-Е10", 92.90m),
        ("95 Mustang Євро5-Е10", 95.90m), ("100 Mustang Євро5-Е0", 102.90m),
        ("ГАЗ", 51.50m), ("AdBlue", 61.90m)
    ];

    [Fact]
    public async Task AGradeWeDoNotSell_IsCreatedUnderTheBrandsOwnWords()
    {
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(UpgSheet), "upg", DryRun: false), Guid.Empty, null);

        // Nothing is seeded in this test, so every published grade is new to us.
        result.Created.Select(c => c.Name).Should().BeEquivalentTo(
            ["upg100", "upg95", "A-95", "EURO DIESEL", "upgDIESEL", "AdBlue", "Газ"]);

        _context.ChangeTracker.Clear();
        foreach (var created in result.Created)
        {
            var fuel = await _context.FuelTypes.SingleAsync(f => f.BrandFuelCode == created.Code);
            // The name is the brand's own token. Nothing translates `upgDIESEL` into ДП: UPG does not
            // call it that, and a name we invented here is a name an operator has to correct later.
            fuel.Name.Should().Be(created.Code);
            // The pump is the reference - the list price the customer's saving is measured against.
            // The column price is written - it is the reference, and it is the one figure the brand
            // published. The customer price is not written: we know what the brand charges and
            // nothing about what we pay, so nobody has priced this fuel yet. A zero would claim we
            // sell it for nothing.
            fuel.PumpPricePerLiter.Should().Be(created.PumpPerLiter);
            fuel.BasePrice.Should().BeNull();
            fuel.DiscountPrice.Should().BeNull();
            fuel.MarginUahPerLiter.Should().BeNull("no acquisition cost is known yet, so there is no margin");
        }
    }

    [Fact]
    public async Task ANewGradeGetsAnOfferAndNominals_SoItIsNotAPriceOfZero()
    {
        await _context.SaveChangesAsync();

        await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(("upg100", 99.90m)), "upg", DryRun: false), Guid.Empty, null);

        _context.ChangeTracker.Clear();
        var fuel = await _context.FuelTypes.SingleAsync(f => f.BrandFuelCode == "upg100");

    fuel.PumpPricePerLiter.Should().Be(99.90m);
    fuel.SupplierPricePerLiter.Should().BeNull();
        var liters = await _context.FuelNominals
    .Where(v => v.FuelTypeId == fuel.Id).Select(v => v.Liters).OrderBy(l => l).ToListAsync();
        liters.Should().BeEquivalentTo([10, 20, 50]);
    }

    [Fact]
    public async Task TwoFuelsOfOneName_ArePricedByTheirOwnCode()
    {
        // UPG publishes A-95 at 88.90 and upg95 at 91.90. These are two products, not one row typed
        // twice, and a name-based or category-based fallback would hand both prices to whichever one
        // the query returned first.
        SeedFuel("upg-a95", BrandIds.Upg, "A-95", "A-95", cost: 83.90m, profit: 5m, pump: 89.00m);
        SeedFuel("upg-95", BrandIds.Upg, "upg95", "upg95", cost: 86.90m, profit: 5m, pump: 92.00m);
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(UpgSheet), "upg", DryRun: false), Guid.Empty, null);

        _context.ChangeTracker.Clear();
    var a95 = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("upg-a95"));
    var upg95 = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("upg-95"));

    a95.PumpPricePerLiter.Should().Be(88.90m);
    upg95.PumpPricePerLiter.Should().Be(91.90m);
    }

    [Fact]
    public async Task AnExistingFuelKeepsItsNameAndMargin()
    {
        var fuel = SeedFuel("wog-dp", BrandIds.Wog, "ДП Євро5", "ДП Євро5", cost: 93.80m, profit: 5m, pump: 103.50m);
        await _context.SaveChangesAsync();

        await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(WogSheet), "wog", DryRun: false), Guid.Empty, null);

        _context.ChangeTracker.Clear();
        var after = await _context.FuelTypes.SingleAsync(f => f.Id == fuel.Id);
        // The two fields a third party must never be able to move: the name a human recognises the
        // fuel by, and the commercial term that decides what we earn.
        after.Name.Should().Be("ДП Євро5");
        after.MarginUahPerLiter.Should().Be(5m);
    }

    [Fact]
    public async Task AnUnchangedSheet_WritesNothingAtAll()
    {
        SeedFuel("upg-95", BrandIds.Upg, "upg95", "upg95", cost: 86.90m, profit: 5m, pump: 91.90m);
        await _context.SaveChangesAsync();

        await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(("upg95", 91.90m)), "upg", DryRun: false), Guid.Empty, null);

        _context.ChangeTracker.Clear();
        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("upg-95"));
        var beforeStamp = fuel.UpdatedAtUtc;

        var second = await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(("upg95", 91.90m)), "upg", DryRun: false), Guid.Empty, null);

        _context.ChangeTracker.Clear();
        var after = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("upg-95"));

        second.Created.Should().BeEmpty();
        second.Repriced.Should().ContainSingle().Which.Changed.Should().BeFalse();
        after.UpdatedAtUtc.Should().Be(beforeStamp, "an unchanged sheet is not an edit");
    }
    [Fact]
    public async Task AFuelNobodyHasPriced_IsNotGivenAPriceOfZero()
    {
        // The state every fuel on the catalogue is in today: no supplier cost, no margin. The pump
        // moved, so the reprice path runs - and it must not write a price of zero, because a price of
        // zero IS a price: it says the customer takes the fuel for nothing, and the database refuses
        // the row (ck_fuel_types_prices_non_negative). The old code wrote one on every fuel it
        // touched, and fourteen of them on production carried it.
        SeedFuel("upg-100", BrandIds.Upg, "upg100", "upg100", cost: null, profit: null, pump: 90.00m);
        await _context.SaveChangesAsync();

        await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(("upg100", 99.90m)), "upg", DryRun: false), Guid.Empty, null);

        _context.ChangeTracker.Clear();
        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("upg-100"));

        // The pump is known, and it is written - it is the reference, not the customer price.
        fuel.PumpPricePerLiter.Should().Be(99.90m);
        // And the price the customer pays is not written at all any more: it is computed from the
        // knobs, and the copies are not what it comes from. What matters here is that nothing writes
        // a zero - a price of zero IS a price, and it says the customer takes the fuel for nothing.
        fuel.BasePrice.Should().NotBe(0m);
        fuel.DiscountPrice.Should().NotBe(0m);

        // And the other half of it: a fuel that IS priced keeps the price the formula gives it. The
        // fix must not turn every price into a null.
        SeedFuel("upg-95", BrandIds.Upg, "upg95", "upg95", cost: 86.90m, profit: 5m, pump: 91.90m);
        await _context.SaveChangesAsync();

        await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(("upg95", 95.00m)), "upg", DryRun: false), Guid.Empty, null);

        _context.ChangeTracker.Clear();
        var priced = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("upg-95"));
        priced.PumpPricePerLiter.Should().Be(95.00m);
        // The price the customer pays is the formula's answer, not a stored number.
        FuelPricing.FinalPerLiterOrNull(86.90m, 5m).Should().Be(91.90m, "cost + margin");
    }

    [Fact]
    public async Task ADryRun_ReportsWhatItWouldDo_AndChangesNothing()
    {
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(UpgSheet), "upg", DryRun: true), Guid.Empty, null);

        result.Created.Should().HaveCount(7);
        result.DryRun.Should().BeTrue();
        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Upg)).Should().Be(0);
    }

    [Fact]
    public async Task OneBrandIsNeverPricedFromAnothersSheet()
    {
        SeedFuel("wog-dp", BrandIds.Wog, "ДП Євро5", "ДП Євро5", cost: 93.80m, profit: 5m, pump: 103.50m);
        await _context.SaveChangesAsync();

        // The UPG sheet has no `ДП Євро5`, so WOG must come out untouched rather than half-matched.
        await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(UpgSheet), "upg", DryRun: false), Guid.Empty, null);

        _context.ChangeTracker.Clear();
        var wog = await _context.FuelTypes.SingleAsync(f => f.BrandId == BrandIds.Wog);
        wog.BasePrice.Should().Be(103.50m);
        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Upg)).Should().Be(7);
    }

    [Fact]
    public async Task ABrandWeDoNotServe_IsRefusedBeforeAnythingIsParsed()
    {
        var result = await Handler().HandleAsync(
            new ImportUpgWogPricesCommand(Sheet(("upg95", 91.90m)), "klo", DryRun: false), Guid.Empty, null);

        result.Errors.Should().ContainSingle();
        result.Touched.Should().Be(0);
        (await _context.FuelTypes.CountAsync()).Should().Be(0);
    }
}
