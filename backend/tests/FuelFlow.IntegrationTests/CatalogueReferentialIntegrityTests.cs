using System.Data;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The catalogue's own referential shape: a fuel belongs to a brand, a price row belongs to a fuel
/// and to a brand, a product code belongs to a fuel and to a brand - and a grade's selling price is
/// a positive number.
/// </summary>
/// <remarks>
/// <para>
/// All four relationships existed only as comments. The data was clean every time it was checked,
/// which is exactly why nothing noticed: an unconstrained column is indistinguishable from a
/// constrained one until a row gets through, and a row that gets through is a price history
/// pointing at a fuel that is not there, or a QR code that resolves to nothing.
/// </para>
/// <para>
/// The delete rules are asserted alongside the constraints because they disagree on purpose.
/// <c>fuel_type_product_codes</c> cascades - those are lookup rows derived from a fuel, the same as
/// <c>fuel_nominals</c>. <c>fuel_price_history</c> restricts, because it is a price history and
/// losing rows quietly is worse than failing loudly.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class CatalogueReferentialIntegrityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CatalogueReferentialIntegrityTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    public static TheoryData<string, string, string> ExpectedForeignKeys => new()
    {
        { "fuel_types", "brand_id", "RESTRICT" },
        { "fuel_price_history", "brand_id", "RESTRICT" },
        { "fuel_price_history", "fuel_type_id", "RESTRICT" },
        { "fuel_type_product_codes", "brand_id", "RESTRICT" },
        { "fuel_type_product_codes", "fuel_type_id", "CASCADE" },
    };

    [Theory]
    [MemberData(nameof(ExpectedForeignKeys))]
    public async Task The_Catalogue_Column_Is_A_ForeignKey(string table, string column, string onDelete)
    {
        await using var context = CreateContext();

        var rule = await context.Database.SqlQuery<string>(
            $"""
             SELECT CASE c.confdeltype WHEN 'c' THEN 'CASCADE' WHEN 'r' THEN 'RESTRICT'
                                      WHEN 'n' THEN 'SET NULL' ELSE 'NO ACTION' END AS value
             FROM pg_constraint c
             JOIN pg_class t ON t.oid = c.conrelid
             JOIN pg_namespace n ON n.oid = t.relnamespace
             JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
             WHERE n.nspname = 'public' AND t.relname = {table} AND a.attname = {column}
               AND c.contype = 'f'
             """).ToListAsync();

        rule.Should().ContainSingle(
            $"{table}.{column} carries no foreign key, so a row naming a {column} that does not " +
            "exist is accepted and only discovered by whatever reads it next");
        rule[0].Should().Be(onDelete);
    }

    [Fact]
    public async Task A_Price_History_Row_Naming_An_Unknown_Fuel_Is_Refused()
    {
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var row = NewPriceHistoryRow(brandId, fuelTypeId: "no-such-fuel-exists-anywhere");

        var act = async () => { context.FuelPriceHistory.Add(row); await context.SaveChangesAsync(); };

        await AssertRefusedAsync(act, PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_Product_Code_Naming_An_Unknown_Brand_Is_Refused()
    {
        await using var context = CreateContext();
        var fuelTypeId = await SeededFuelTypeId(context);

        var code = NewProductCode(fuelTypeId, brandId: Guid.NewGuid(), productCode: "ZZZZ");

        var act = async () => { context.FuelTypeProductCodes.Add(code); await context.SaveChangesAsync(); };

        await AssertRefusedAsync(act, PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_Zero_Discount_Price_Is_Allowed()
    {
        // Zero is a price state, not a violation. A grade the network lists but nobody has priced
        // yet has a published pump price, no cost and no margin, and a discount price of 0 - which
        // is exactly what the OKKO catalogue import leaves behind, and exactly what
        // UnpricedPackageIntegrationTests.A_Nominal_With_Neither_Rate_Nor_Margin_Is_Not_Charged
        // builds. A row-level CHECK that forbade it would refuse every grade the catalogue sync
        // creates.
        //
        // What keeps such a grade off the shelf is not the column: its price computes to null,
        // and the storefront does not list a package with no price. This test is here so that the
        // next person to "fix" the zero does not have to rediscover that.
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-unpriced", brandId);
        fuel.DiscountPrice = 0m;
        fuel.PumpPricePerLiter = 92.90m;

        await context.Set<FuelTypeEntity>().AddAsync(fuel);
        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync();

        await context.Set<FuelTypeEntity>().Where(f => f.Id == ToUuid("catalogue-unpriced")).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A_Negative_Discount_Price_Is_Refused()
    {
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-negative-price", brandId);
        fuel.DiscountPrice = -1m;

        var act = async () => { context.Set<FuelTypeEntity>().Add(fuel); await context.SaveChangesAsync(); };

        await AssertRefusedAsync(act, PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_Positive_Discount_Price_Is_Accepted()
    {
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-positive-price", brandId);
        fuel.DiscountPrice = 1m;

        await context.Set<FuelTypeEntity>().AddAsync(fuel);
        await context.SaveChangesAsync();

        (await context.Set<FuelTypeEntity>()
            .IgnoreQueryFilters()
            .SingleAsync(f => f.Id == ToUuid("catalogue-positive-price"))).DiscountPrice
            .Should().Be(1m);

        await context.Set<FuelTypeEntity>().Where(f => f.Id == ToUuid("catalogue-positive-price")).ExecuteDeleteAsync();
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// The write must fail, and it must fail in the database rather than in the change tracker. The
    /// SQLSTATE is what says which constraint did it: a unique violation and a foreign-key violation
    /// both surface as <see cref="DbUpdateException"/>, and this is the difference between "the
    /// constraint I added" and "some other constraint happened to be there".
    /// </summary>
    private static async Task AssertRefusedAsync(Func<Task> write, string expectedSqlState)
    {
        var thrown = await write.Should().ThrowAsync<DbUpdateException>();

        var postgres = thrown.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        postgres.SqlState.Should().Be(
            expectedSqlState,
            "the constraint under test is the one that has to refuse the write");
    }

    private static async Task<Guid> SeededBrand(ApplicationDbContext context)
    {
        var id = await context.Brands.IgnoreQueryFilters().Select(b => (Guid?)b.Id).FirstOrDefaultAsync();
        id.Should().NotBeNull("the fixture seeds the four networks");
        return id!.Value;
    }

    private static async Task<string> SeededFuelTypeId(ApplicationDbContext context)
    {
        var id = await context.Set<FuelTypeEntity>().IgnoreQueryFilters().Select(f => f.Id).FirstOrDefaultAsync();
        id.Should().NotBe(Guid.Empty, "the fixture seeds the catalogue");
        return id.ToString();
    }

    /// <summary>
    /// The switch that says "this fuel may be sold below cost" needs a cost to be below.
    /// </summary>
    /// <remarks>
    /// All twenty-three fuels carried the switch on, and none of them had a supplier cost, so it
    /// was a switch that disarmed the below-cost guard for nothing - and became a live one the
    /// moment a cost was recorded. The rule belongs in the schema: the switch can only be on for
    /// a fuel whose cost is known.
    /// </remarks>
    [Fact]
    public async Task A_Fuel_Without_A_Cost_May_Not_Be_Flagged_Sellable_Below_Cost()
    {
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-below-cost-without-a-cost", brandId);
        fuel.AllowBelowCost = true;
        fuel.SupplierPricePerLiter = null;

        var act = async () => { context.Set<FuelTypeEntity>().Add(fuel); await context.SaveChangesAsync(); };

        await AssertRefusedAsync(act, PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_Fuel_With_A_Cost_May_Be_Flagged_Sellable_Below_Cost()
    {
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-below-cost-with-a-cost", brandId);
        fuel.AllowBelowCost = true;
        fuel.SupplierPricePerLiter = 45m;

        context.Set<FuelTypeEntity>().Add(fuel);
        await context.SaveChangesAsync();

        (await context.Set<FuelTypeEntity>()
            .IgnoreQueryFilters()
            .SingleAsync(f => f.Id == ToUuid("catalogue-below-cost-with-a-cost"))).AllowBelowCost
            .Should().BeTrue("a manager who has priced the fuel may allow it to be sold below cost");

        await context.Set<FuelTypeEntity>()
            .Where(f => f.Id == ToUuid("catalogue-below-cost-with-a-cost"))
            .ExecuteDeleteAsync();
    }

    private static FuelTypeEntity NewFuel(string id, Guid brandId) => new()
    {
        Id =   ToUuid(id),
        BrandId = brandId,
        Name = id,
        BasePrice = 50m,
        DiscountPrice = 50m,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private static FuelPriceHistory NewPriceHistoryRow(Guid brandId, string fuelTypeId) => new()
    {
        Id = Guid.NewGuid(),
        BrandId = brandId,
        FuelTypeId =   ToUuid(fuelTypeId),
        DataKind = "real",
        EffectiveFromUtc = DateTime.UtcNow,
        RecordedAtUtc = DateTime.UtcNow,
        Reason = "operator",
    };

    private static FuelTypeProductCode NewProductCode(string fuelTypeId, Guid brandId, string productCode) => new()
    {
        FuelTypeId =   ToUuid(fuelTypeId),
        BrandId = brandId,
        ProductCode = productCode,
    };
}