using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.ImportKloPrices;
using FuelFlow.Features.Pricing.ScrapeMinfinPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
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
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
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
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
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
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
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
}
