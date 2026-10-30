using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.ImportMinfinPrices;
using FuelFlow.Features.Pricing.ScrapeMinfinPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// The Minfin price import: the table's row for a network, matched against our catalogue of it.
/// </summary>
/// <remarks>
/// Two networks, two matching rules. KLO's catalogue is named after the columns, so an operator's
/// fuel and Minfin's column are compared by the grade they speak of. WOG names a grade in its own
/// words, so the correspondence is written out in <see cref="ScrapeMinfinPrices.MinfinNetwork"/> and
/// the tests below pin it against the names production actually holds.
/// </remarks>
public sealed class ImportMinfinPricesCommandHandlerTests : IDisposable
{
    private const string Klo = "klo";
    private const string Wog = "wog";

    private readonly ApplicationDbContext _context;

    public ImportMinfinPricesCommandHandlerTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private ImportMinfinPricesCommandHandler Handler()
        => new(_context, new PriceHistoryRecorder(_context), new BrandSlugResolver(_context));

    /// <summary>The sheet, as Minfin publishes it for KLO on 2026-10-19.</summary>
    private static readonly MinfinFuelPrice[] Sheet =
    [
        new("А 95+", 95.60m),
        new("А 95", 87.90m),
        new("А 92", 86.50m),
        new("ДП", 101.90m),
        new("Газ", 50.90m),
    ];

    /// <summary>The WOG row, as Minfin published it on 2026-10-30: five columns, A-92 empty.</summary>
    private static readonly MinfinFuelPrice[] WogSheet =
    [
        new("А 95+", 95.90m),
        new("А 95", 92.90m),
        new("ДП", 106.90m),
        new("Газ", 58.90m),
    ];

    /// <summary>
    /// WOG's catalogue as production holds it, with the prices its own page last wrote and the codes
    /// that page gave it.
    /// </summary>
    private static readonly (string Name, decimal Price)[] WogCatalogue =
    [
        ("95 Євро5-Е10", 92.90m),
        ("95 Mustang Євро5-Е10", 95.90m),
        ("100 Mustang Євро5-Е0", 102.90m),
        ("ДП Євро5", 106.90m),
        ("ДП Mustang+", 109.90m),
        ("ГАЗ", 58.90m),
        ("AdBlue", 61.90m),
    ];

    private async Task SeedBrandAsync()
        => await SeedBrandAsync(BrandIds.Klo, Klo);

    private async Task SeedBrandAsync(Guid brandId, string slug)
    {
        _context.Brands.Add(new Brand
        {
            Id = brandId,
            Slug = slug,
            Name = slug,
            LogoText = slug,
            Color = "#000",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    /// <summary>
    /// The catalogue as production held it before the catalogue was emptied: five KLO fuels named
    /// after Minfin's columns, none of them carrying a code or a price.
    /// </summary>
    private async Task SeedOperatorFuelsAsync(params string[] names)
    {
        foreach (var name in names)
        {
            _context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = Guid.CreateVersion7(),
                BrandId = BrandIds.Klo,
                Name = name,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task It_should_match_a_fuel_by_name_even_when_the_spelling_differs()
    {
        await SeedBrandAsync();
        // The operator wrote "А95"; Minfin's column says "А 95". Same grade.
        await SeedOperatorFuelsAsync("А95", "Газ");

        var result = await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, Sheet, DryRun: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        var a95 = await _context.FuelTypes.SingleAsync(f => f.Name == "А95");
        a95.PumpPricePerLiter.Should().Be(87.90m, "the column's price is the price of this grade");
        // A fuel with no code cannot resolve by code, and this is the only source that can say which
        // grade it is - so it is given one.
        a95.BrandFuelCode.Should().Be("А 95");
        // The two grades we carry were not duplicated: the sheet's "А 95" found our "А95", and
        // "Газ" found our "Газ".
        (await _context.FuelTypes.CountAsync(f => f.Name == "А95")).Should().Be(1);
        (await _context.FuelTypes.CountAsync(f => f.Name == "Газ")).Should().Be(1);
        // The three we do not carry were created.
        (await _context.FuelTypes.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task It_should_match_a_fuel_by_grade_even_when_the_script_or_the_punctuation_differs()
    {
        await SeedBrandAsync();
        // The operator wrote "А-95" and Minfin's column says "А 95": the same grade, written with a
        // hyphen and a Latin letter. The comparison used to ignore whitespace and case only, so
        // this was a second row for a grade the catalogue already held.
        await SeedOperatorFuelsAsync("А-95", "ДП", "Газ");

        await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, Sheet, DryRun: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        var a95 = await _context.FuelTypes.SingleAsync(f => f.Name == "А-95");
        a95.PumpPricePerLiter.Should().Be(87.90m, "the column's price is the price of this grade");
        a95.BrandFuelCode.Should().Be("А 95", "and the code makes it resolve exactly from now on");

        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Klo)).Should().Be(5,
            "one grade is one row: the sheet's 'А 95' found our 'А-95', and it did not write a second one");
    }

    [Fact]
    public async Task It_should_create_the_grades_the_catalogue_lacks()
    {
        await SeedBrandAsync();
        await SeedOperatorFuelsAsync("А95");

        var result = await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, Sheet, DryRun: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        result.Created.Should().HaveCount(4);
        var created = await _context.FuelTypes.Where(f => f.BrandId == BrandIds.Klo).ToListAsync();
        created.Should().HaveCount(5);
        created.Should().OnlyContain(f => f.PumpPricePerLiter != null);
        // With the sizes, which is what puts a grade on the shelf.
        (await _context.FuelNominals.CountAsync()).Should().Be(12);
    }

    [Fact]
    public async Task It_should_not_create_a_grade_when_the_refresh_is_only_allowed_to_move_prices()
    {
        await SeedBrandAsync();
        await SeedOperatorFuelsAsync("А95");

        await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, Sheet, DryRun: false, CreateMissing: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        (await _context.FuelTypes.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task It_should_never_touch_the_cost_or_the_margin()
    {
        await SeedBrandAsync();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("klo-a95"),
            Name = "А95",
            BrandId = BrandIds.Klo,
            SupplierPricePerLiter = 40m,
            MarginUahPerLiter = 3m,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, Sheet, DryRun: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        // Minfin knows what the driver pays and nothing about what we pay.
        var fuel = await _context.FuelTypes.SingleAsync(f => f.Id == ToUuid("klo-a95"));
        fuel.SupplierPricePerLiter.Should().Be(40m);
        fuel.MarginUahPerLiter.Should().Be(3m);
        fuel.PumpPricePerLiter.Should().Be(87.90m);
    }
    [Fact]
    public async Task It_should_refuse_an_empty_sheet()
    {
        await SeedBrandAsync();
        await SeedOperatorFuelsAsync("А95");

        var act = async () => await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, [], DryRun: false), Guid.Empty, "test");

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an empty sheet is a page that failed to load, not a network that stopped selling fuel");
    }

    /// <summary>
    /// A reprice leaves the evidence the column exists for: the fuel's own stamp and one history row.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-29 the importer saved first and recorded after, and the recorder deliberately
    /// does not save - its callers own the transaction - so every stamp and every row this import
    /// wrote died with the request scope. Production showed a KLO diesel that had moved 104.40 to
    /// 105.40 in the log while `price_updated_at` still carried the seed date and
    /// `fuel_price_history` held one row per fuel, also from the seed. A second run over an unchanged
    /// sheet must not append a second row: the import runs every fifteen minutes.
    /// </remarks>
    [Fact]
    public async Task A_reprice_stamps_the_fuel_and_appends_one_history_row()
    {
        await SeedBrandAsync();
        await SeedOperatorFuelsAsync("А 95+", "А 95", "А 92", "ДП", "Газ");
        await SetPumpPricesAsync(("ДП", 104.40m));

        await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, Sheet, DryRun: false, CreateMissing: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        var diesel = await _context.FuelTypes.SingleAsync(f => f.Name == "ДП");
        diesel.PumpPricePerLiter.Should().Be(101.90m, "the column's price is written to the fuel");
        diesel.PriceUpdatedAt.Should().Be(TestClock.UtcNow,
            "a reprice stamps the fuel, and the stamp is what the freshness alarm reads");

        var history = await _context.FuelPriceHistory.Where(h => h.FuelTypeId == diesel.Id).ToListAsync();
        history.Should().ContainSingle("one reprice is one history row, and the row survives the save");
        history[0].Reason.Should().Be(PriceHistoryReasons.PumpImport);
        history[0].PumpPricePerLiter.Should().Be(101.90m);
        history[0].EffectiveFromUtc.Should().Be(TestClock.UtcNow);

        (await _context.FuelPriceHistory.CountAsync()).Should().Be(5,
            "no fuel had a row to compare against, so the first run lays down one baseline per matched grade");

        // The next tick, over the same sheet. Nothing moved, so nothing is appended - but the run
        // still stamps, because "the source still lists this fuel" is a fact worth keeping.
        await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, Sheet, DryRun: false, CreateMissing: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        (await _context.FuelPriceHistory.CountAsync()).Should().Be(5,
            "an unchanged sheet must not bury the history in a row per fuel per quarter hour");
    }

    [Fact]
    public async Task A_dry_run_writes_neither_a_stamp_nor_a_history_row()
    {
        await SeedBrandAsync();
        await SeedOperatorFuelsAsync("А 95+", "А 95", "А 92", "ДП", "Газ");

        await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Klo, Sheet, DryRun: true, CreateMissing: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        (await _context.FuelPriceHistory.CountAsync()).Should().Be(0);
        (await _context.FuelTypes.Where(f => f.PriceUpdatedAt != null).CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// WOG's catalogue as production holds it, with the prices its own page last wrote and the codes
    /// that page gave it.
    /// </summary>
    private async Task SeedWogCatalogueAsync()
    {
        foreach (var (name, price) in WogCatalogue)
        {
            _context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = Guid.CreateVersion7(),
                BrandId = BrandIds.Wog,
                Name = name,
                BrandFuelCode = name,
                PumpPricePerLiter = price,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    /// <summary>
    /// WOG is matched through its own vocabulary: the column says "А 95+" and the shelf says
    /// "95 Mustang Євро5-Е10", and the correspondence is written out rather than folded.
    /// </summary>
    [Fact]
    public async Task Wog_grades_are_matched_through_the_brands_own_words()
    {
        await SeedBrandAsync(BrandIds.Wog, Wog);
        await SeedWogCatalogueAsync();
        await SetPumpPricesAsync(("ДП Євро5", 105.00m));

        var result = await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Wog, WogSheet, DryRun: false, CreateMissing: false),
            Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        var fuels = await _context.FuelTypes.Where(f => f.BrandId == BrandIds.Wog).ToListAsync();
        fuels.Single(f => f.Name == "95 Mustang Євро5-Е10").PumpPricePerLiter.Should().Be(95.90m,
            "Minfin's 'А 95+' is WOG's premium 95, and the two agreed on the figure on 2026-10-30");
        fuels.Single(f => f.Name == "95 Євро5-Е10").PumpPricePerLiter.Should().Be(92.90m);
        fuels.Single(f => f.Name == "ДП Євро5").PumpPricePerLiter.Should().Be(106.90m);
        fuels.Single(f => f.Name == "ГАЗ").PumpPricePerLiter.Should().Be(58.90m);

        // No column for these three, so nothing to write: they keep what WOG's page last said, and
        // the import says which they are instead of leaving the freeze unsaid.
        fuels.Single(f => f.Name == "100 Mustang Євро5-Е0").PumpPricePerLiter.Should().Be(102.90m);
        fuels.Single(f => f.Name == "ДП Mustang+").PumpPricePerLiter.Should().Be(109.90m);
        fuels.Single(f => f.Name == "AdBlue").PumpPricePerLiter.Should().Be(61.90m);
        result.GradesNotListed.Should().BeEquivalentTo(
            ["100 Mustang Євро5-Е0", "ДП Mustang+", "AdBlue"]);

        result.Repriced.Single(r => r.Name == "ДП Євро5").Changed.Should().BeTrue();
        fuels.Should().HaveCount(7, "Minfin prices the shelf; it does not add a row to it");
        fuels.Single(f => f.Name == "95 Євро5-Е10").BrandFuelCode.Should().Be("95 Євро5-Е10",
            "the column is not WOG's code for the grade, so it must not become one");
    }

    [Fact]
    public async Task A_wog_column_with_no_counterpart_is_reported_rather_than_written()
    {
        await SeedBrandAsync(BrandIds.Wog, Wog);
        await SeedWogCatalogueAsync();

        var result = await Handler().HandleAsync(
            new ImportMinfinPricesCommand(
                Wog, [.. WogSheet, new MinfinFuelPrice("А 92", 86.50m)], DryRun: false, CreateMissing: false),
            Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        // WOG does not sell A-92 today, and the cell is empty; should the column fill in one day, a
        // fuel named after it would be a shelf row invented by a table.
        result.GradesNotMapped.Should().BeEquivalentTo(["А 92"]);
        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Wog)).Should().Be(7);
        (await _context.FuelTypes.AnyAsync(f => f.PumpPricePerLiter == 86.50m)).Should().BeFalse();
    }

    [Fact]
    public async Task A_wog_grade_is_never_created_from_a_column_name()
    {
        await SeedBrandAsync(BrandIds.Wog, Wog);
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = Guid.CreateVersion7(),
            BrandId = BrandIds.Wog,
            Name = "95 Євро5-Е10",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await Handler().HandleAsync(
            new ImportMinfinPricesCommand(Wog, WogSheet, DryRun: false, CreateMissing: true),
            Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        // Even on the pass that is allowed to grow a catalogue: "А 95+" is Minfin's word, and a WOG
        // fuel created under it would be a grade the brand does not sell under that name.
        result.Created.Should().BeEmpty();
        (await _context.FuelTypes.CountAsync(f => f.BrandId == BrandIds.Wog)).Should().Be(1);
        (await _context.FuelTypes.SingleAsync()).BrandFuelCode.Should().BeNull(
            "a fuel with no code keeps none: WOG's codes carry the brand's wording, not the column's");
    }

    private async Task SetPumpPricesAsync(params (string Name, decimal Price)[] prices)
    {
        foreach (var (name, price) in prices)
        {
            var fuel = await _context.FuelTypes.SingleAsync(f => f.Name == name);
            fuel.PumpPricePerLiter = price;
        }

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }
}
