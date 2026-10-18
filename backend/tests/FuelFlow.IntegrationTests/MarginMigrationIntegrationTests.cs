using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>20261018081344_MarginLivesOnTheFuel</c> copies every live offer's margin onto its fuel.
/// <para>
/// The copy is the whole risk of that migration: the column is nullable, so a copy that matches nothing
/// leaves every fuel with no margin — and a fuel with no margin has <em>no customer price at all</em>,
/// because treating a missing margin as zero would publish a free fuel. A migration that runs cleanly
/// and leaves the catalogue unpriced looks identical to one that ran correctly until somebody sells
/// something, which is why it is checked here against a database.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class MarginMigrationIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MarginMigrationIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task EveryLiveFuelWithAMarginOnItsOffer_AlsoHasOneOnTheFuel()
    {
        await using var context = CreateContext();

        var onOffers = await context.Database.SqlQueryRaw<string>(
            """
            SELECT DISTINCT fuel_type_id
              FROM fuel_offers
             WHERE NOT is_deleted
               AND margin_uah_per_liter IS NOT NULL
            """).ToListAsync();

        onOffers.Should().NotBeEmpty(
            "the migration would have nothing to copy if the catalogue carried no margins at all, "
            + "which would make the rest of this test vacuous");

        var fuelsWithoutMargin = await context.FuelTypes
            .Where(f => onOffers.Contains(f.Id) && f.MarginUahPerLiter == null)
            .Select(f => f.Id)
            .ToListAsync();

        fuelsWithoutMargin.Should().BeEmpty(
            "these fuels are priced on their offer but not on themselves, so the admin would show a "
            + "fuel with no margin that the storefront is still selling at one");
    }

    [Fact]
    public async Task AFuelTakesItsLivePoolsMargin_NotARehearsalOnes()
    {
        await using var context = CreateContext();

        // One real offer priced, and - if the catalogue has a rehearsal pool - a QA one priced higher.
        var real = await context.FuelOffers
            .Where(o => !o.IsDeleted && o.DataKind == SharedKernel.Domain.DataKinds.Real
                && o.MarginUahPerLiter != null)
            .Select(o => new { o.FuelTypeId, o.MarginUahPerLiter })
            .ToListAsync();

        real.Should().NotBeEmpty();

        foreach (var offer in real)
        {
            var fuel = await context.FuelTypes
                .Where(f => f.Id == offer.FuelTypeId)
                .Select(f => new { f.MarginUahPerLiter })
                .FirstAsync();

            fuel.MarginUahPerLiter.Should().Be(
                offer.MarginUahPerLiter,
                "a rehearsal margin read as a live one would price customers off the wrong pool");
        }
    }
}