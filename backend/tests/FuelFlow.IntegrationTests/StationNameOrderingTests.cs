using FluentAssertions;
using FuelFlow.Features.Brands.GetPublicBrandStationsByBrand;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A station list reads in the order a person would write it.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue's names carry a number - "OKKO №01", "OKKO №10", "OKKO №100" - and a byte sort
/// puts them in the order "№01", "№09", "№1", "№10", "№100", "№11", because "1" sorts before "2".
/// On production, 598 of the 1,322 stations have a name with a number in it, so this is most of
/// the list rather than an edge case.
/// </para>
/// <para>
/// The order is the database's, not the application's: the admin list is paged, and Skip/Take run
/// in SQL, so a sort applied after the page was cut would put the same stations on two pages and
/// leave others on none. That is why <c>name_sort_key</c> is a generated column with an index on
/// it, and why the handlers order by it rather than by the name.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class StationNameOrderingTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public StationNameOrderingTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The names a network actually uses, in the order it should be read. The zero-padded and the
    /// unpadded forms of the same number are both there because both are in the catalogue, and
    /// because the padded one has to come first - "№01" is the first station, not the eleventh.
    /// </summary>
    private static readonly string[] Numbered = ["№01", "№02", "№09", "№1", "№10", "№100", "№11", "№2", "№20", "№3"];

    [Fact]
    public async Task The_Numbered_Stations_Are_Ordered_Naturally()
    {
        await using var context = CreateContext();
        var brandId = await SeedBrandAsync(context);
        var names = Numbered.Select(n => $"ORDER-TEST {n}").ToArray();

        await using (var seed = CreateContext())
        {
            foreach (var name in names)
            {
                seed.BrandStations.Add(new BrandStation
                {
                    Id = Guid.CreateVersion7(),
                    ExternalId = $"order-test-{Guid.CreateVersion7()}",
                    BrandId = brandId,
                    Name = name,
                    CreatedAtUtc = TestClock.UtcNow,
                    UpdatedAtUtc = TestClock.UtcNow
                });
            }

            await seed.SaveChangesAsync();
        }

        try
        {
            var ordered = await context.BrandStations
                .AsNoTracking()
                .Where(s => s.Name.StartsWith("ORDER-TEST"))
                .OrderBy(s => EF.Property<string>(s, "NameSortKey"))
                .ThenBy(s => s.Name)
                .Select(s => s.Name)
                .ToListAsync();

            // The natural order. "№01" and "№1" are the same number, so their keys are identical
            // and the name breaks the tie - the padded form first, because "0" sorts before "1".
            ordered.Should().Equal(
                "ORDER-TEST №01", "ORDER-TEST №1",
                "ORDER-TEST №02", "ORDER-TEST №2",
                "ORDER-TEST №3", "ORDER-TEST №09",
                "ORDER-TEST №10", "ORDER-TEST №11",
                "ORDER-TEST №20", "ORDER-TEST №100");
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.Database.ExecuteSqlRawAsync(
                "DELETE FROM brand_stations WHERE name LIKE 'ORDER-TEST %'");
        }
    }

    [Fact]
    public async Task The_Station_List_Endpoint_Returns_Them_In_That_Order()
    {
        // Through the handler, not through the context: the point is that the ORDER BY a client
        // actually receives is the natural one. A test that queries the column itself would pass
        // even if every handler still ordered by the name.
        await using var context = CreateContext();
        var brandId = await SeedBrandAsync(context);
        var names = Numbered.Select(n => $"ORDER-TEST {n}").ToArray();

        await using (var seed = CreateContext())
        {
            foreach (var name in names)
            {
                seed.BrandStations.Add(new BrandStation
                {
                    Id = Guid.CreateVersion7(),
                    ExternalId = $"order-test-{Guid.CreateVersion7()}",
                    BrandId = brandId,
                    Name = name,
                    CreatedAtUtc = TestClock.UtcNow,
                    UpdatedAtUtc = TestClock.UtcNow
                });
            }

            await seed.SaveChangesAsync();
        }

        try
        {
            var handler = new GetPublicBrandStationsByBrandQueryHandler(context);
            var listed = await handler.HandleAsync(new GetPublicBrandStationsByBrandQuery(brandId));

            listed.Where(s => s.Name.StartsWith("ORDER-TEST"))
                .Select(s => s.Name)
                .Should().Equal(
                    "ORDER-TEST №01", "ORDER-TEST №1",
                    "ORDER-TEST №02", "ORDER-TEST №2",
                    "ORDER-TEST №3", "ORDER-TEST №09",
                    "ORDER-TEST №10", "ORDER-TEST №11",
                    "ORDER-TEST №20", "ORDER-TEST №100");
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.Database.ExecuteSqlRawAsync(
                "DELETE FROM brand_stations WHERE name LIKE 'ORDER-TEST %'");
        }
    }

    [Fact]
    public async Task The_Sort_Key_Is_What_The_Database_Stores()
    {
        // The key is a column, not something the application computes, so this asserts the column
        // itself: a number run padded to a fixed width, so that the byte order of the key is the
        // order a person writes.
        await using var context = CreateContext();

        var keys = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT fuelflow_natural_sort_key('№1') AS value
                UNION ALL SELECT fuelflow_natural_sort_key('№10')
                UNION ALL SELECT fuelflow_natural_sort_key('№100')
                UNION ALL SELECT fuelflow_natural_sort_key('№2')
                """)
            .ToListAsync();

        keys.Should().BeEquivalentTo(
            ["№0000000001", "№0000000010", "№0000000100", "№0000000002"],
            "the padding has to be a FIXED width - a variable one sorts '№2' before '№10'");
    }

    [Fact]
    public async Task The_Column_Exists_And_Is_Indexed()
    {
        await using var context = CreateContext();

        var rows = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.relname
                  FROM pg_class c
                  JOIN pg_namespace n ON n.oid = c.relnamespace
                 WHERE n.nspname = 'public' AND c.relname = 'ix_brand_stations_name_sort_key'
                """)
            .ToListAsync();

        rows.Should().ContainSingle(
            "the sort key is ordered on in the database, so the index has to exist for it to be cheap");
    }

    private static async Task<Guid> SeedBrandAsync(ApplicationDbContext context)
    {
        var id = await context.Brands.IgnoreQueryFilters().Select(b => (Guid?)b.Id).FirstOrDefaultAsync();
        id.Should().NotBeNull("the fixture seeds the four networks");
        return id!.Value;
    }
}