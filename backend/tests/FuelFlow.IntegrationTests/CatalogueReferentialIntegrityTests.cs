using System.Data;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

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
/// <c>fuel_nominals</c>. <c>fuel_offer_prices</c> restricts, because it is a price history and
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
        { "fuel_offer_prices", "brand_id", "RESTRICT" },
        { "fuel_offer_prices", "fuel_type_id", "RESTRICT" },
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
    public async Task An_Offer_Price_Naming_An_Unknown_Fuel_Is_Refused()
    {
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var offer = NewOfferPrice(brandId, fuelTypeId: "no-such-fuel-exists-anywhere");

        var act = async () => { context.FuelOfferPrices.Add(offer); await context.SaveChangesAsync(); };

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
    public async Task A_Zero_Discount_Price_Alongside_A_Pump_Price_Is_Refused()
    {
        // A fuel that knows its pump price and still sells at zero is the 1 UAH leak: OKKO
        // "Пульс 95" carried discount_price 0.00 against a pump price of 95.90, because a NULL
        // final_price_per_liter was copied across as 0.
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-zero-price", brandId);
        fuel.DiscountPrice = 0m;
        fuel.PumpPricePerLiter = 95.90m;

        var act = async () => { context.Set<FuelTypeEntity>().Add(fuel); await context.SaveChangesAsync(); };

        await AssertRefusedAsync(act, PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_Zero_Discount_Price_With_No_Pump_Price_Is_Allowed()
    {
        // Zero is a price. A giveaway grade sells at nothing and carries no pump price to
        // contradict - DatabaseIntegrityTests.A_Zero_Price_Volume_Is_Allowed_But_Zero_Liters_Is_Not
        // says so on purpose, and forbidding zero outright was wrong.
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-giveaway", brandId);
        fuel.DiscountPrice = 0m;
        fuel.PumpPricePerLiter = null;

        await context.Set<FuelTypeEntity>().AddAsync(fuel);
        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync();

        await context.Set<FuelTypeEntity>().Where(f => f.Id == "catalogue-giveaway").ExecuteDeleteAsync();
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
            .SingleAsync(f => f.Id == "catalogue-positive-price")).DiscountPrice
            .Should().Be(1m);

        await context.Set<FuelTypeEntity>().Where(f => f.Id == "catalogue-positive-price").ExecuteDeleteAsync();
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
        id.Should().NotBeNull("the fixture seeds the catalogue");
        return id!;
    }

    private static FuelTypeEntity NewFuel(string id, Guid brandId) => new()
    {
        Id = id,
        BrandId = brandId,
        Name = id,
        BasePrice = 50m,
        DiscountPrice = 50m,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private static FuelOfferPrice NewOfferPrice(Guid brandId, string fuelTypeId) => new()
    {
        Id = Guid.NewGuid(),
        BrandId = brandId,
        FuelTypeId = fuelTypeId,
        DataKind = "real",
        EffectiveFromUtc = DateTime.UtcNow,
        RecordedAtUtc = DateTime.UtcNow,
        Reason = "operator",
    };

    private static FuelTypeProductCode NewProductCode(string fuelTypeId, Guid brandId, string productCode) => new()
    {
        FuelTypeId = fuelTypeId,
        BrandId = brandId,
        ProductCode = productCode,
    };
}