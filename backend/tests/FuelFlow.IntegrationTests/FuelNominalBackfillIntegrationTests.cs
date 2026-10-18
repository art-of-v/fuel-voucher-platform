using Npgsql;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_nominals</c> is where "this fuel is sold in these sizes" lives from now on, so the backfill
/// has to carry the catalogue across or the shelf empties itself the moment the code stops reading
/// volumes.
/// </summary>
[Collection("Integration Tests")]
public sealed class FuelNominalBackfillIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelNominalBackfillIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task EveryLiveVolume_BecomesANominalOfItsFuel()
    {
        await using var context = CreateContext();

        // The pairs the backfill had to copy, straight from the table it read.
        var fromVolumes = await (from v in context.FuelOfferVolumes
                                 join o in context.FuelOffers on v.OfferId equals o.Id
                                 where !v.IsDeleted && !o.IsDeleted && v.Liters > 0
                                 select new { o.FuelTypeId, Liters = v.Liters })
            .Distinct()
            .ToListAsync();

        fromVolumes.Should().NotBeEmpty("a fresh catalogue has no volumes to copy and this test proves nothing");

        var nominals = await context.FuelNominals
            .AsNoTracking()
            .Select(n => new { n.FuelTypeId, n.Liters })
            .ToListAsync();

        nominals.Should().Contain(fromVolumes,
            "a nominal that is not listed after the backfill is a size the operator chose which has vanished from the shelf");
    }

    [Fact]
    public async Task ANominal_IsOneRowPerFuelAndSize_NotPerOffer()
    {
        await using var context = CreateContext();

        // The property the whole change exists for: two pools of one fuel agree on the sizes, because
        // there is only one place to say so.
        var duplicated = await (from n in context.FuelNominals
                                group n by new { n.FuelTypeId, n.Liters } into g
                                where g.Count() > 1
                                select g.Key)
            .ToListAsync();

        duplicated.Should().BeEmpty("the primary key is (fuel_type_id, liters), so this cannot happen - unless the key was dropped");
    }

    [Fact]
    public async Task ANegativeSize_IsRefused()
    {
        await using var context = CreateContext();

        var fuel = await context.FuelTypes.AsNoTracking().Select(f => f.Id).FirstAsync();

        var act = () => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO fuel_nominals (fuel_type_id, liters, created_at_utc, updated_at_utc)
            VALUES ({0}, -10, now(), now())
            """, fuel);

        await act.Should().ThrowAsync<PostgresException>(
            "the CHECK is the point - it is the database refusing, not the test fixture");
    }
}