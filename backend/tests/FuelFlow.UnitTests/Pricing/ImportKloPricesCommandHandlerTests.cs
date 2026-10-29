using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.ImportKloPrices;
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
/// The KLO price import: Minfin's table, matched against a catalogue an operator built by hand.
/// </summary>
public sealed class ImportKloPricesCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public ImportKloPricesCommandHandlerTests()
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

    private ImportKloPricesCommandHandler Handler()
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

    private async Task SeedBrandAsync()
    {
        _context.Brands.Add(new Brand
        {
            Id = BrandIds.Klo,
            Slug = "klo",
            Name = "klo",
            LogoText = "klo",
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
            new ImportKloPricesCommand(Sheet, DryRun: false), Guid.Empty, "test");
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
            new ImportKloPricesCommand(Sheet, DryRun: false), Guid.Empty, "test");
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
            new ImportKloPricesCommand(Sheet, DryRun: false), Guid.Empty, "test");
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
            new ImportKloPricesCommand(Sheet, DryRun: false, CreateMissing: false), Guid.Empty, "test");
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
            new ImportKloPricesCommand(Sheet, DryRun: false), Guid.Empty, "test");
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
            new ImportKloPricesCommand([], DryRun: false), Guid.Empty, "test");

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
            new ImportKloPricesCommand(Sheet, DryRun: false, CreateMissing: false), Guid.Empty, "test");
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
            new ImportKloPricesCommand(Sheet, DryRun: false, CreateMissing: false), Guid.Empty, "test");
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
            new ImportKloPricesCommand(Sheet, DryRun: true, CreateMissing: false), Guid.Empty, "test");
        _context.ChangeTracker.Clear();

        (await _context.FuelPriceHistory.CountAsync()).Should().Be(0);
        (await _context.FuelTypes.Where(f => f.PriceUpdatedAt != null).CountAsync()).Should().Be(0);
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
