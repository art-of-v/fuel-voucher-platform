using FluentAssertions;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A nominal with nothing to price it from: the offer exists, the fuel has no margin, so there is
/// no number to charge.
/// </summary>
/// <remarks>
/// There used to be a second way out - <c>fuel_offer_volumes.price_override_uah</c>, a price
/// somebody typed by hand - and it is gone. Every price in the catalogue is now a derivation from
/// the fuel's rate and the offer's ceiling, so the only remaining question is what happens when
/// there is nothing to derive from. It must be a refusal, never a zero: a zero on the shelf is a
/// free fuel, and a zero on an invoice is a sale that cost money.
/// <para>
/// The arithmetic is covered by <c>PricingFormulaTests</c>; what can only be checked here is that
/// a real database agrees - that the column is really absent, and that a package built from these
/// rows really is unpriced rather than accidentally priced by some default.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class UnpricedPackageIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public UnpricedPackageIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task A_Nominal_With_Neither_Rate_Nor_Margin_Is_Not_Charged()
    {
        await using var context = CreateContext();
        var fuelId = $"unpriced-fuel-{Guid.CreateVersion7()}";

        var fuel = new FuelTypeEntity
        {
            Id = fuelId,
            Name = "Unpriced test fuel",
            BrandId = BrandIds.Okko,
            BasePrice = 0m,
            DiscountPrice = 0m,
            // What the OKKO catalogue import leaves behind: the pump is published, nobody has set a
            // cost or a margin, so there is no price to charge.
            PumpPricePerLiter = 92.90m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.FuelTypes.Add(fuel);

        var volume = new FuelNominal
        {
            FuelTypeId = fuelId,
            Liters = 10m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.FuelNominals.Add(volume);
        await context.SaveChangesAsync();

        var package = SellablePackage.From(fuel, DataKinds.Real, volume.Liters, "okko");

        package.ComputedFinalPricePerLiter.Should().BeNull();
        FuelPricing.TryPackagePrice(50m, null, 92.90m, 1m, 10m).Should().BeNull();

        var act = () => ServerPricing.PackagePrice(package, 10m);
        act.Should().Throw<InvalidOperationException>(
            "there is no price to charge, and charging nothing is not an option");
    }

    /// <summary>
    /// The schema is the claim: the column that let a price be typed by hand is not merely unused,
    /// it is gone. Asserted against the database rather than the entity, because a column can be
    /// mapped and still exist.
    /// </summary>
    [Fact]
    public async Task The_Per_Package_Price_Column_Is_Gone()
    {
        await using var context = CreateContext();

        var exists = await context.Database.SqlQueryRaw<string>(
            """
            SELECT count(*)::text AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'fuel_offer_volumes' AND column_name = 'price_override_uah'
            """).SingleAsync();

exists.Should().Be("0");
    }
}
