using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_types.brand_fuel_code</c> — the BRAND's own code for a grade, unique per brand among
/// live rows. Planning #349, step 3: the stable half of a fuel's identity, which the price import now
/// matches on.
/// </summary>
/// <remarks>
/// <para>
/// The database half of the guarantee belongs here rather than in a unit test, because the whole point
/// is an index: <c>ux_fuel_types_station_brand_fuel_code</c> refuses two live fuels claiming one code at
/// one brand, and nothing in C# has to be trusted to say so.
/// </para>
/// <para>
/// Two properties that are easy to get wrong and expensive to discover late, so both are stated here:
/// <b>null does not collide with null</b> (a fuel entered by hand has no code, and two of them at one
/// station must both fit) and <b>the same code at two brands is fine</b> (WOG and OKKO both sell
/// A-95; the code is per brand because the brand is what spells it).
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FuelTypeBrandFuelCodeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelTypeBrandFuelCodeTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The five seeded OKKO fuels and the code OKKO publishes for each, read off
    /// <c>https://www.okko.ua/api/uk/fuels</c> on 2026-10-17. Shared by the tests below so the mapping
    /// is stated once: two copies of it would be one too many place to forget an update.
    /// </summary>
    private static readonly Dictionary<string, string> SeedCodeMap = new()
    {
        ["okko-95"] = "A-95",
        ["okko-dp"] = "DP",
        ["okko-p95"] = "Pulls 95",
        ["okko-pulls-dp"] = "Pulls Diesel",
        ["okko-gas"] = "SPBT",
    };

    private static FuelTypeEntity Fuel(string id, string stationId, string name, string? code)
        => new()
        {
            Id = id,
            StationId = stationId,
            Name = name,
            BrandFuelCode = code,
            BasePrice = 50m,
            DiscountPrice = 48m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task TwoLiveFuels_CannotClaimTheSameCodeAtOneBrand()
    {
        // The ambiguity the import would otherwise resolve arbitrarily: one code, two fuels, and
        // whichever row it picks becomes the fuel a voucher gets priced against.
        await using var context = CreateContext();
        var now = DateTime.UtcNow;

        context.FuelTypes.Add(Fuel("code-a", "okko", "ДП ЄВРО (code test)", "9018"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.FuelTypes.Add(Fuel("code-b", "okko", "ДП ЄВРО (code test 2)", "9018"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_types WHERE id IN ('code-a', 'code-b')");
    }

    [Fact]
    public async Task TheSameCode_AtTwoBrands_IsNotADuplicate()
    {
        // "A-95" is OKKO's spelling and WOG's. The code identifies a fuel *within* a brand, which
        // is why the index leads with station_id and why a global unique on the code would be wrong.
        //
        // "A-95 EXTRA" rather than "A-95": the seeded OKKO A-95 now carries "A-95" (step 6), and a test
        // that reused it would be refused by the index for the right reason at the wrong moment —
        // which is how a test stops testing what its name says.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("net-a", "okko", "A-95 (code test)", "A-95 EXTRA"));
        context.FuelTypes.Add(Fuel("net-b", "wog", "A-95 Mustang (code test)", "A-95 EXTRA"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_types WHERE id IN ('net-a', 'net-b')");
    }

    [Fact]
    public async Task TwoFuelsWithNoCode_DoNotCollide()
    {
        // The reason the index excludes NULL rather than treating every absent code as one value: a
        // hand-entered fuel and a rehearsal fuel are both legitimately code-less.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("nocode-a", "upg", "Паливо без коду (code test)", null));
        context.FuelTypes.Add(Fuel("nocode-b", "upg", "Ще одне без коду (code test)", null));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_types WHERE id IN ('nocode-a', 'nocode-b')");
    }

    [Fact]
    public async Task AnArchivedFuel_FreesItsCode()
    {
        // Same shape as ux_fuel_types_station_name: the index is partial on NOT is_deleted, so taking a
        // fuel out of the catalogue releases its code for the fuel that replaces it.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("arch-code-a", "klo", "ДП (code reuse test)", "9018"));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE fuel_types SET is_deleted = true, deleted_at_utc = now() WHERE id = 'arch-code-a'");

        context.ChangeTracker.Clear();
        context.FuelTypes.Add(Fuel("arch-code-b", "klo", "ДП (code reuse test)", "9018"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM fuel_types WHERE id IN ('arch-code-a', 'arch-code-b')");
    }

    [Fact]
    public async Task OnlyTheOperatorCreatedFuels_StillHaveNoCode()
    {
        // Documents the migration's decision rather than its effect: nothing was backfilled by NAME,
        // because a code guessed from a fuel name is the wrong identity, and a unique index then
        // enforces the wrong one. The five seeded OKKO fuels are the deliberate exception — their codes
        // were read off OKKO's published list, keyed by seed id.
        await using var context = CreateContext();

        var uncoded = await context.FuelTypes
            .IgnoreQueryFilters()
            .Where(f => f.BrandFuelCode == null)
            .Select(f => f.Id)
            .ToListAsync();

        uncoded.Should().OnlyContain(id => !SeedCodeMap.ContainsKey(id),
            "a seeded OKKO fuel carries the code OKKO publishes; everything else is filled by hand or by the import");
    }

    [Fact]
    public async Task TheSeededOkkoFuels_CarryTheCodesOkkoPublishes()
    {
        // Read off /api/uk/fuels on 2026-10-17, not guessed from the names: the seed calls one of them
        // "ДП ЄВРО" and OKKO calls that grade "DP", which is exactly why a name match would have been
        // a guess. Asserted against a migrated database, so a seed edit that drops one is caught here.
        await using var context = CreateContext();

        var seeded = (await context.FuelTypes
                .Where(f => f.StationId == "okko")
                .ToListAsync())
            .Where(f => SeedCodeMap.ContainsKey(f.Id))
            .ToDictionary(f => f.Id, f => f.BrandFuelCode);

        SeedCodeMap.Keys.Should().AllSatisfy(id => seeded.Should().ContainKey(id),
            "the seed installs these five and nothing else at OKKO");

        foreach (var (id, code) in SeedCodeMap)
            seeded[id].Should().Be(code, "a freshly migrated catalogue must be born keyed");
    }

    [Fact]
    public async Task TheSeededWogFuels_CarryNoCode()
    {
        // The restraint, asserted as carefully as the mapping: nobody has ever shown us WOG's
        // vocabulary, so a code here would be a guess about a supplier's naming — and a guess is an
        // identity the unique index would then enforce. They price by category until a WOG source is
        // proven.
        await using var context = CreateContext();

        var wog = await context.FuelTypes
            .Where(f => f.StationId == "wog")
            .Select(f => f.BrandFuelCode)
            .ToListAsync();

        wog.Should().NotBeEmpty("the seed still installs them");
        wog.Should().AllSatisfy(code => code.Should().BeNull());
    }

    [Fact]
    public async Task AnOverlongCode_IsRefused()
    {
        // 50 is generous for "Pulls 95" or a product code, but the column is indexed and unique, so it
        // is bounded rather than free text — the same call as every other catalog column (F-24).
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("long-code", "okko", "Довгий код (code test)", new string('x', 51)));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM fuel_types WHERE id = 'long-code'");
    }
}