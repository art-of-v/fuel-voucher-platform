using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The seeded networks' ids, after the migration that re-pointed production at the derived ones.
/// </summary>
/// <remarks>
/// <para>
/// The derivation is only a convenience if the database agrees with it. A fresh database is seeded
/// with the derived ids by EF, and a migrated one was re-pointed at them by
/// <c>20261019201500_SeededBrandIdsBecomeDerived</c> — two different paths to the same state, and
/// this is what says they landed there.
/// </para>
/// <para>
/// The second half is the part a scaffolded migration would have missed: the rows in
/// <c>fuel_types</c>, <c>fuel_type_product_codes</c>, <c>contracts</c>, <c>brand_stations</c> and
/// <c>fuel_price_history</c> that name a network. The seed's own rows are here; a production row
/// is not, and the migration re-points it by the same join.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class SeededBrandIdsAreDerivedTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SeededBrandIdsAreDerivedTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task Every_seeded_network_carries_its_derived_id()
    {
        await using var context = CreateContext();

        var rows = await context.Brands
            .IgnoreQueryFilters()
            .Select(b => new { b.Slug, b.Id })
            .ToListAsync();

        rows.Should().Contain(r => r.Slug == "okko" && r.Id == BrandIds.Okko);
        rows.Should().Contain(r => r.Slug == "wog" && r.Id == BrandIds.Wog);
        rows.Should().Contain(r => r.Slug == "upg" && r.Id == BrandIds.Upg);
        rows.Should().Contain(r => r.Slug == "klo" && r.Id == BrandIds.Klo);
    }

    [Fact]
    public async Task No_network_carries_one_of_the_constants_the_derivation_replaced()
    {
        await using var context = CreateContext();

        var replaced = new[]
        {
            new Guid("11111111-1111-4111-8111-111111111111"),
            new Guid("22222222-2222-4222-8222-222222222222"),
            new Guid("33333333-3333-4333-8333-333333333333"),
            new Guid("44444444-4444-4444-8444-444444444444"),
        };

        var ids = await context.Brands.IgnoreQueryFilters().Select(b => b.Id).ToListAsync();

        ids.Should().NotIntersectWith(replaced);
    }

    [Fact]
    public async Task The_fuels_the_seed_writes_name_their_network_by_its_derived_id()
    {
        await using var context = CreateContext();

        var rows = await context.FuelTypes
            .IgnoreQueryFilters()
            .Select(f => new { f.Id, f.BrandId })
            .ToListAsync();

        rows.Should().Contain(f => f.Id == ToUuid("okko-95") && f.BrandId == BrandIds.Okko);
        rows.Should().Contain(f => f.Id == ToUuid("wog-95") && f.BrandId == BrandIds.Wog);
    }

    [Fact]
    public async Task A_row_may_name_a_network_by_its_derived_id()
    {
        await using var context = CreateContext();

        context.FuelTypeProductCodes.Add(new FuelTypeProductCode
        {
            FuelTypeId = ToUuid("okko-95"),
            BrandId = BrandIds.Okko,
            ProductCode = "9018-derived",
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "the foreign key into brands must accept the id the derivation hands out");

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM fuel_type_product_codes WHERE product_code = '9018-derived'");
    }

    /// <summary>
    /// The migration immediately before this one - the schema production is in today.
    /// </summary>
    private const string BeforeThisMigration = "20261019150000_QueryStatisticsAreCollected";

    /// <summary>
    /// The id the OKKO network carried before the derivation, and the one it carries after it.
    /// </summary>
    private static readonly Guid OldOkkoId = new("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task A_row_that_already_existed_is_re_pointed_at_the_derived_id()
    {
        // The production sequence, in order. The seed's own rows are what a fresh database has; a
        // production row - a station of a network, a contract, a recorded price - is not in the
        // seed at all, and a migration that only re-points what it seeded would leave every one of
        // them naming an id that no longer exists.
        var stationId = Guid.NewGuid();

        await WithSchemaFrom(BeforeThisMigration, async context =>
        {
            await context.Database.ExecuteSqlRawAsync($"""
                INSERT INTO brand_stations
                    (id, external_id, brand_id, name, created_at_utc, updated_at_utc, is_deleted)
                VALUES
                    ('{stationId}', 'okko-50.45010-30.52340', '{OldOkkoId}', 'АЗК', now(), now(), false);
                """);
        });

        // Back at head: the row names the network by the id the network now carries.
        await using var context = CreateContext();

        var brandId = await context.Database
            .SqlQueryRaw<Guid>($"SELECT brand_id AS \"Value\" FROM brand_stations WHERE id = '{stationId}'")
            .SingleAsync();

        brandId.Should().Be(BrandIds.Okko);
    }

    /// <summary>
    /// Runs <paramref name="body"/> against the schema as it stood before
    /// <paramref name="migration"/>, and puts the schema back at head afterwards.
    /// </summary>
    /// <remarks>
    /// The shape <see cref="DataKindBackfillTests"/> uses, and for the same reason: a migration is
    /// only observed on rows that were already there, and the only way to see that is to roll the
    /// schema back, write the rows the way production wrote them, and roll forward. The schema is
    /// returned to head in a <c>finally</c>, so one failure cannot cascade into the next.
    /// </remarks>
    private async Task WithSchemaFrom(string migration, Func<ApplicationDbContext, Task> body)
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(migration);
            await body(context);
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }
}
