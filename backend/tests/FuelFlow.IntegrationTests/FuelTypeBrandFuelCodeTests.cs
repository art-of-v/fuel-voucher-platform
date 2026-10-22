using FuelFlow.Features.Brands;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_types.brand_fuel_code</c> — the BRAND's own code for a grade, unique per brand among
/// live rows. Planning #349, step 3: the stable half of a fuel's identity, which the price import now
/// matches on.
/// </summary>
/// <remarks>
/// <para>
/// The database half of the guarantee belongs here rather than in a unit test, because the whole point
/// is an index: <c>ux_fuel_types_Brand_brand_fuel_code</c> refuses two live fuels claiming one code at
/// one brand, and nothing in C# has to be trusted to say so.
/// </para>
/// <para>
/// Two properties that are easy to get wrong and expensive to discover late, so both are stated here:
/// <b>null does not collide with null</b> (a fuel entered by hand has no code, and two of them at one
/// Brand must both fit) and <b>the same code at two brands is fine</b> (WOG and OKKO both sell
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
       private static readonly Dictionary<Guid, string> SeedCodeMap = new()
       {
           [ToUuid("okko-95")] = "A-95",
           [ToUuid("okko-dp")] = "DP",
           [ToUuid("okko-p95")] = "Pulls 95",
           [ToUuid("okko-pulls-dp")] = "Pulls Diesel",
           [ToUuid("okko-gas")] = "SPBT",
       };

    private static FuelTypeEntity Fuel(string id, Guid brandId, string name, string? code)
        => new()
        {
            Id =   ToUuid(id),
            BrandId = brandId,
            Name = name,
            BrandFuelCode = code,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    [Fact]
    public async Task TwoLiveFuels_CannotClaimTheSameCodeAtOneBrand()
    {
        // The ambiguity the import would otherwise resolve arbitrarily: one code, two fuels, and
        // whichever row it picks becomes the fuel a voucher gets priced against.
        await using var context = CreateContext();
        var now = TestClock.UtcNow;

        context.FuelTypes.Add(Fuel("$1", BrandIds.Okko, "ДП ЄВРО (code test)", "9018"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.FuelTypes.Add(Fuel("$1", BrandIds.Okko, "ДП ЄВРО (code test 2)", "9018"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await context.Database.ExecuteSqlRawAsync($"DELETE FROM fuel_types WHERE id IN ('{ToUuid("code-a")}', '{ToUuid("code-b")}')");
    }

    [Fact]
    public async Task TheSameCode_AtTwoBrands_IsNotADuplicate()
    {
        // "A-95" is OKKO's spelling and WOG's. The code identifies a fuel *within* a brand, which
        // is why the index leads with brand_id and why a global unique on the code would be wrong.
        //
        // "A-95 EXTRA" rather than "A-95": the seeded OKKO A-95 now carries "A-95" (step 6), and a test
        // that reused it would be refused by the index for the right reason at the wrong moment —
        // which is how a test stops testing what its name says.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("net-a", BrandIds.Okko, "A-95 (code test)", "A-95 EXTRA"));
        context.FuelTypes.Add(Fuel("net-b", BrandIds.Wog, "A-95 Mustang (code test)", "A-95 EXTRA"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync($"DELETE FROM fuel_types WHERE id IN ('{ToUuid("net-a")}', '{ToUuid("net-b")}')");
    }

    [Fact]
    public async Task TwoFuelsWithNoCode_DoNotCollide()
    {
        // The reason the index excludes NULL rather than treating every absent code as one value: a
        // hand-entered fuel and a rehearsal fuel are both legitimately code-less.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("nocode-a", BrandIds.Upg, "Паливо без коду (code test)", null));
        context.FuelTypes.Add(Fuel("nocode-b", BrandIds.Upg, "Ще одне без коду (code test)", null));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync($"DELETE FROM fuel_types WHERE id IN ('{ToUuid("nocode-a")}', '{ToUuid("nocode-b")}')");
    }

    [Fact]
    public async Task AnArchivedFuel_FreesItsCode()
    {
        // Same shape as ux_fuel_types_Brand_name: the index is partial on NOT is_deleted, so taking a
        // fuel out of the catalogue releases its code for the fuel that replaces it.
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("arch-code-a", BrandIds.Klo, "ДП (code reuse test)", "9018"));
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE fuel_types SET is_deleted = true, deleted_at_utc = now() WHERE id = '{ToUuid("arch-code-a")}'");

        context.ChangeTracker.Clear();
        context.FuelTypes.Add(Fuel("arch-code-b", BrandIds.Klo, "ДП (code reuse test)", "9018"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM fuel_types WHERE id IN ('{ToUuid("arch-code-a")}', '{ToUuid("arch-code-b")}')");
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
                .Where(f => f.BrandId == BrandIds.Okko)
                .ToListAsync())
            .Where(f => SeedCodeMap.ContainsKey(f.Id))
            .ToDictionary(f => f.Id, f => f.BrandFuelCode);

        SeedCodeMap.Keys.Should().AllSatisfy(id => seeded.Should().ContainKey(id),
            "the seed installs these five and nothing else at OKKO");

        foreach (var (id, code) in SeedCodeMap)
            seeded[id].Should().Be(code, "a freshly migrated catalogue must be born keyed");
    }

    [Fact]
    public async Task ASeededWogFuelWhoseNameIsNotWogsVocabulary_CarriesNoCode()
    {
        // The restraint, restated for a world where we finally have WOG's vocabulary.
        //
        // It used to read "nobody has ever shown us WOG's vocabulary", which was true and is now
        // false — the brand publishes one. But knowing the vocabulary is not the same as having our
        // rows named after it: WOG prints `ДП Mustang+`, `95 Mustang Євро5-Е10` and
        // `100 Mustang Євро5-Е0`, while the seeded fuels are named `ДП Mustang`, `A-95 Mustang`,
        // `A 95 EURO` and `Mustang 100`. Coding from the name would write four codes WOG has never
        // published, and the unique index would then enforce those guesses as identities.
        //
        // So the rule is the same, with the reason corrected: a fuel is coded when its name is a
        // token the brand actually publishes, and is not otherwise. The seeded ones mostly are not.
        await using var context = CreateContext();

        var wog = await context.FuelTypes
.Where(f => f.BrandId == BrandIds.Wog)
            .Select(f => new { f.Name, f.BrandFuelCode })
            .ToListAsync();

        wog.Should().NotBeEmpty("the seed still installs them");

        // WOG publishes this one verbatim, so it is coded — and only it.
        wog.Where(f => f.Name == "ГАЗ")
            .Should().AllSatisfy(f => f.BrandFuelCode.Should().Be("ГАЗ"));

        // The rest of the seed is not WOG's vocabulary, and stays uncoded.
        wog.Where(f => f.Name != "ГАЗ")
            .Should().AllSatisfy(f => f.BrandFuelCode.Should().BeNull(
                "coding a name the brand does not publish is a guess the index would then enforce"));
    }

    [Fact]
    public async Task AHandTypedWogFuelNamedAsPublished_CarriesThatCode()
    {
        // The production shape, and the reason the migration above can exist at all: an operator
        // typed these off WOG's own sheet, so the name already is the token.
        await using var context = CreateContext();
        var now = TestClock.UtcNow;

        context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = ToUuid("wog-dp-e5"), Name = "ДП Євро5", BrandId = BrandIds.Wog, CreatedAtUtc = now, UpdatedAtUtc = now },
            new FuelTypeEntity { Id = ToUuid("wog-95-e5-e10"), Name = "95 Євро5-Е10", BrandId = BrandIds.Wog, CreatedAtUtc = now, UpdatedAtUtc = now });
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlRawAsync(
            $"""
            UPDATE fuel_types SET brand_fuel_code = btrim(name)
            WHERE brand_id = '{BrandIds.Wog}' AND brand_fuel_code IS NULL AND NOT is_deleted
              AND btrim(name) IN ('ДП Євро5', 'ДП Mustang+', '95 Євро5-Е10', '95 Mustang Євро5-Е10', '100 Mustang Євро5-Е0', 'ГАЗ', 'AdBlue')
            """);

        context.ChangeTracker.Clear();
        var coded = await context.FuelTypes
            .Where(f => f.Id == ToUuid("wog-dp-e5") || f.Id == ToUuid("wog-95-e5-e10"))
            .Select(f => new { f.Name, f.BrandFuelCode })
            .ToListAsync();

        coded.Select(f => f.BrandFuelCode).Should().BeEquivalentTo(["ДП Євро5", "95 Євро5-Е10"]);
    }

    [Fact]
    public async Task AnOverlongCode_IsRefused()
    {
        // 50 is generous for "Pulls 95" or a product code, but the column is indexed and unique, so it
        // is bounded rather than free text — the same call as every other catalog column (F-24).
        await using var context = CreateContext();

        context.FuelTypes.Add(Fuel("$1", BrandIds.Okko, "Довгий код (code test)", new string('x', 51)));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await context.Database.ExecuteSqlRawAsync($"DELETE FROM fuel_types WHERE id = '{ToUuid("long-code")}'");
    }
}