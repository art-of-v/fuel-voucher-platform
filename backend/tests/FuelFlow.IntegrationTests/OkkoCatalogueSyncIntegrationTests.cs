using FluentAssertions;
using FuelFlow.BackgroundJobs;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.BackfillBrandFuelCodes;
using FuelFlow.Features.Pricing.ImportOkkoCatalogue;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The OKKO catalogue and pricing steps, run against <b>a real Postgres</b>, in the order and over the
/// <b>one shared context</b> the scheduler runs them.
/// <para>
/// Everything above this class tested those steps on the in-memory provider, which has no unique
/// indexes and therefore never refused a write. That is why a defect could sit in a merged pull
/// request, through a green unit suite, and only appear in production: with fifteen fuels and not one
/// brand code, the catalogue step judged every published grade missing and tried to create "ГАЗ" beside
/// the "ГАЗ" that was already there. Postgres refused it on <c>ux_fuel_types_Brand_name</c>, the
/// refused rows stayed attached to the shared context, and the backfill step then tried to insert them
/// again - which is how the same incident reported itself twice, the second time as an unrelated clash
/// on a brand code.
/// </para>
/// <para>
/// Both properties are therefore asserted here, against the indexes that actually exist: a grade we
/// already sell by name is not created a second time, and a step that does fail cannot poison the next.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class OkkoCatalogueSyncIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private static readonly Guid OkkoBrandId = BrandIds.Okko;

    private readonly TestDatabaseFixture _fixture;

    public OkkoCatalogueSyncIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private sealed class StubClient(IReadOnlyList<OkkoPumpPrice> sheet) : IOkkoPriceClient
    {
        public Task<IReadOnlyList<OkkoPumpPrice>> FetchAsync(CancellationToken ct = default)
            => Task.FromResult(sheet);
    }

    /// <summary>
    /// The scheduler's wiring: one context, three handlers, in the order
    /// <see cref="OkkoPriceSyncService"/> calls them. Built by hand rather than resolved so that the
    /// sharing - the thing under test - is visible in the test.
    /// </summary>
    private static OkkoPriceSyncService Sync(
        ApplicationDbContext context,
        IReadOnlyList<OkkoPumpPrice> sheet,
        ILogger<OkkoPriceSyncService>? logger = null)
    {
        var events = new AuditEventService(context);
        var priceRecorder = new PriceHistoryRecorder(context);
        return new OkkoPriceSyncService(
            new StubClient(sheet),
            new ImportOkkoPumpPricesCommandHandler(context, new BrandSlugResolver(context), events, priceRecorder),
            new ImportOkkoCatalogueCommandHandler(context, new BrandSlugResolver(context), events, priceRecorder),
            new BackfillBrandFuelCodesCommandHandler(context, new BrandSlugResolver(context), events),
            context,
            Options.Create(new OkkoPriceOptions { Enabled = true }),
            logger ?? NullLogger<OkkoPriceSyncService>.Instance);
    }

    /// <summary>
    /// Production's actual state, not the migration seed's: fuels an operator typed, every one of them
    /// named the way the operator typed it, and not a single one carrying a brand code.
    /// <para>
    /// The seeded rows are deleted first, deliberately. Migrations seed <c>okko-gas</c> as "ГАЗ" and
    /// <c>okko-dp</c> as "ДП ЄВРО", and a fresh database still has both - production has neither, which
    /// is why the catalogue step found no code to match and no way to tell a grade it already sells from
    /// one it had never seen. Testing against the seed would hide exactly that: the seeded rows carry
    /// codes, so a code-only import matches them and never reaches the duplicate.
    /// </para>
    /// </summary>
    private async Task SeedOperatorTypedFuelsAsync(ApplicationDbContext context)
    {
        var seededFuels = await context.FuelTypes
            .Where(f => f.BrandId == OkkoBrandId)
            .ToListAsync();

        if (seededFuels.Count > 0)
        {
            var seededIds = seededFuels.Select(f => f.Id).ToList();

            // The sizes are the fuel's and the seeded catalogue already states them; nothing per
            // offer has to be torn down here any more. The price history hangs off the fuel, so it
            // goes before the fuel it belongs to.
            context.FuelPriceHistory.RemoveRange(await context.FuelPriceHistory
                .Where(p => seededIds.Contains(p.FuelTypeId))
                .ToListAsync());

            context.FuelTypes.RemoveRange(seededFuels);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        var now = TestClock.UtcNow;
        if (!await context.Brands.AnyAsync(s => s.Id == OkkoBrandId))
        {
            context.Brands.Add(new Brand
            {
                Id = OkkoBrandId, Name = "okko", Color = "#000", LogoText = "okko"
            });
        }

        context.FuelTypes.AddRange(
            new FuelTypeEntity
            {
                Id = ToUuid("prod-gas"), Name = "ГАЗ", BrandId =  OkkoBrandId,
                CreatedAtUtc = now, UpdatedAtUtc = now
            },
            new FuelTypeEntity
            {
                Id = ToUuid("prod-a95"), Name = "А95 ЄВРО", BrandId =  OkkoBrandId,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// The production incident, reduced to its one cause, in the form the grade rule now makes
    /// impossible: OKKO renders the LPG grade as "ГАЗ" and as a code, the catalogue carries a fuel called
    /// "ГАЗ" with no code, and a test that asks only "do we know this code?" cannot tell the import
    /// there is nothing to add.
    /// </summary>
    [Fact]
    public async Task AGradeWeAlreadySellByName_IsNotCreatedASecondTime()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedOperatorTypedFuelsAsync(context);

        var sheet = new List<OkkoPumpPrice> { new("SPBT", 45.90m, "ГАЗ") };

        var logged = new CollectingLogger();
        await Sync(context, sheet, logged).SyncPricesAsync();
        ForgetTrackedEntities(context);

        var fuels = await context.FuelTypes
            .Where(f => f.BrandId == OkkoBrandId)
            .ToListAsync();

        // The refusal happened every fifteen minutes on production and left no trace in the catalogue -
        // the insert was refused, so the row count is the same either way. The log is the only place
        // the defect was ever visible, which is why the assertion has to be here rather than on the rows.
        logged.Errors.Should().BeEmpty(
            "nothing in this catalogue should fail against the indexes that exist");

        fuels.Should().HaveCount(
            2, "we already sell the one grade on this sheet, under the very name OKKO prints for it");

        fuels.Count(f => f.Name == "ГАЗ").Should().Be(
            1, "and under no circumstances two of them: the second one is what Postgres refused, "
            + "fifteen minutes at a time");

        // A second tick has to be as quiet and as pointless as the first.
        var secondTick = new CollectingLogger();
        using var second = _fixture.Services.CreateScope();
        var again = second.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Sync(again, sheet, secondTick).SyncPricesAsync();
        ForgetTrackedEntities(again);

        secondTick.Errors.Should().BeEmpty();

        (await again.FuelTypes.CountAsync(f => f.BrandId == OkkoBrandId))
            .Should().Be(2, "a second tick must be as idempotent as the first");
    }

    /// <summary>
    /// The duplicate the grade rule exists to prevent, against the index that would let it through:
    /// "ДП PULLS" already means the diesel the sheet publishes as "Pulls Diesel", and neither carries a
    /// code on a fresh catalogue. Code-only matching finds nothing and adds "ДП" beside it.
    /// </summary>
    [Fact]
    public async Task AGradeWeAlreadySellUnderAnotherName_IsReportedAndNotDuplicated()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedOperatorTypedFuelsAsync(context);

        // Our own spelling of the grade, no code - exactly the state a fresh catalogue is in.
        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("prod-pulls-diesel"), Name = "ДП PULLS", BrandId =  OkkoBrandId,
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow,
        });
        await context.SaveChangesAsync();
        ForgetTrackedEntities(context);

        var sheet = new List<OkkoPumpPrice>
        {
            new("SPBT", 45.90m, "ГАЗ"),
            new("Pulls Diesel", 105.90m, "ДП"),
        };

        await Sync(context, sheet).SyncPricesAsync();
        ForgetTrackedEntities(context);

        (await context.FuelTypes.CountAsync(f => f.BrandId == OkkoBrandId))
            .Should().Be(3, "'ДП' and 'ДП PULLS' are one grade written two ways, not two fuels");

        (await context.FuelTypes.Select(f => f.Name).ToListAsync())
            .Should().NotContain("ДП", "and the name the site printed never reaches the catalogue");
    }

    /// <summary>
    /// A fuel the catalogue genuinely lacks is still created — the rule is one row per grade, not one row
    /// per brand — and it is named from the grade, not from the label the site printed.
    /// </summary>
    [Fact]
    public async Task AGradeNobodySells_IsCreatedUnderItsCanonicalName()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedOperatorTypedFuelsAsync(context);

        // "100 PULLS" classifies as no grade at all, so it holds nothing and blocks nothing.
        var sheet = new List<OkkoPumpPrice>
        {
            new("Pulls 95", 95.90m, "95 PULLS"),
        };

        // The weekly catalogue check is what brings a new grade in; the fifteen-minute price refresh
        // only moves prices. A grade we do not carry is news on a scale of weeks, and creating one
        // is a decision - so it belongs to the job that runs weekly.
        await Sync(context, sheet).SyncCatalogueAndPricesAsync();
        ForgetTrackedEntities(context);

        var created = await context.FuelTypes.SingleAsync(f => f.BrandFuelCode == "Pulls 95");
        created.Name.Should().Be(
            "Пульс 95",
            "'95 PULLS' is a rendering; the code is the identity, and the name follows it");

        (await context.FuelTypes.CountAsync(f => f.BrandId == OkkoBrandId)).Should().Be(3);

        // The sizes, the same three every other importer gives a grade it creates. A size is not a
        // price: it is what puts the grade on the shelf at all, because the storefront lists a fuel
        // by its size. What keeps the fuel off sale is the missing MARGIN, and that is the part no
        // importer has any business guessing.
        var sizes = await context.FuelNominals.AsNoTracking()
            .Where(n => n.FuelTypeId == created.Id)
            .Select(n => n.Liters)
            .OrderBy(l => l)
            .ToListAsync();
        sizes.Should().Equal(10m, 20m, 50m);
    }

    /// <summary>
    /// A name is not an identity - that is why the brand code exists - so the fuel that matched by name
    /// still has no code after the import, and the backfill is what gives it one.
    /// </summary>
    [Fact]
    public async Task TheFuelThatMatchedByName_StillGetsItsCodeFromTheBackfill()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedOperatorTypedFuelsAsync(context);

        var sheet = new List<OkkoPumpPrice>
        {
            new("SPBT", 45.90m, "ГАЗ"),
            new("A-95", 51.90m, "А95 ЄВРО"),
        };

        await Sync(context, sheet).SyncPricesAsync();
        ForgetTrackedEntities(context);

        var gas = await context.FuelTypes.SingleAsync(f => f.Id == ToUuid("prod-gas"));
        gas.BrandFuelCode.Should().Be(
            "SPBT", "matching by name stops the import duplicating the fuel; it does not replace the code");

        var a95 = await context.FuelTypes.SingleAsync(f => f.Id == ToUuid("prod-a95"));
        a95.BrandFuelCode.Should().Be("A-95");
    }

    /// <summary>
    /// The second half of the incident, and the reason the first half was so hard to read: the three
    /// steps share a context, so a step that is refused leaves its rows attached and the next step's save
    /// tries to insert them again - reporting the previous step's clash as its own.
    /// <para>
    /// The failure here is provoked with a constraint the handlers cannot foresee (a CHECK rejecting
    /// one name), which is the general shape of the hazard: a transient fault, a constraint nobody
    /// modelled, a trigger. Prices must still be applied and the backfill must still run.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AFailedCatalogueStep_DoesNotStopTheBackfillOrThePricing()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedOperatorTypedFuelsAsync(context);

        // The constraint is schema, and the schema is the owner's business: the traffic role
        // has no DDL privilege, and it must not have one.
        await using var owner = _fixture.CreateOwnerContext();
        await owner.Database.ExecuteSqlRawAsync(
            "ALTER TABLE fuel_types ADD CONSTRAINT ck_rejects_one_name CHECK (name <> 'ДП')");

        try
        {
            var sheet = new List<OkkoPumpPrice>
            {
                new("SPBT", 45.90m, "ГАЗ"),
                new("DP", 49.90m, "ДП"),
            };

            var logged = new CollectingLogger();
            // The catalogue step and the pricing step live in the same job, and the catalogue runs
            // first: a grade that does not exist yet has nowhere to hold a price.
            await Sync(context, sheet, logged).SyncCatalogueAndPricesAsync();
            ForgetTrackedEntities(context);

            (await context.FuelTypes.CountAsync(f => f.BrandId == OkkoBrandId))
                .Should().Be(2, "the refused insert must not be carried into the next step's save");

            var gas = await context.FuelTypes.SingleAsync(f => f.Id == ToUuid("prod-gas"));
            gas.BrandFuelCode.Should().Be(
                "SPBT", "the backfill runs after the catalogue step and has a right to be unaffected by it");
            gas.PumpPricePerLiter.Should().Be(
                45.90m, "the pump price is the one thing a catalogue failure must not cost us");

            logged.Errors.Should().ContainSingle(
                "the failure is reported once, by the step that actually failed");
        }
        finally
        {
            await owner.Database.ExecuteSqlRawAsync(
                "ALTER TABLE fuel_types DROP CONSTRAINT ck_rejects_one_name");
        }
    }

    /// <summary>
    /// Drops what the sync left tracked, so the assertions below read the database rather than an
    /// in-memory object graph.
    /// <para>
    /// Without this the test lies in exactly the case it exists for: a write that Postgres refused is
    /// still sitting in the change tracker with its new value, and a query on the same context hands
    /// that object straight back. The backfill's code and the new pump price both "appear" applied when
    /// neither reached a row - the test would pass on a production that is failing three times a tick.
    /// </para>
    /// </summary>
    private static void ForgetTrackedEntities(ApplicationDbContext context) => context.ChangeTracker.Clear();

    /// <summary>Collects log entries so a test can assert on what an unattended run said.</summary>
    private sealed class CollectingLogger : ILogger<OkkoPriceSyncService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public List<string> Errors => [.. Entries
            .Where(e => e.Level >= LogLevel.Error)
            .Select(e => e.Message)];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
