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
public async Task Every_Seeded_Fuel_Has_At_Least_One_Nominal()
{
    await using var context = CreateContext();

    // This used to read `fuel_offer_volumes` and prove that the backfill copied every live size
    // across. That table is gone, so the copy can no longer be observed at runtime - the migration
    // that did it is history and still correct, but nothing is left to compare against. What is
    // still observable, and is what actually matters, is that the seeded catalogue has sizes at
    // all: a fuel with no nominal is a fuel the storefront cannot sell, and it would read as an
    // empty catalogue rather than as a missing one.
    var fuelsWithoutASize = await (from f in context.FuelTypes
        where !f.IsDeleted
        && !context.FuelNominals.Any(n => n.FuelTypeId == f.Id)
        select f.Id)
        .ToListAsync();

    fuelsWithoutASize.Should().BeEmpty("a fresh catalogue is seeded with sizes for every live fuel");
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