using System.Data;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
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

    /// <summary>
    /// "Not priced yet" is an absent price, and that is what the cost and the margin beside it have
    /// always said. A grade the network lists but nobody has priced yet has a published pump price,
    /// no cost and no margin - and no customer price, because the customer price is computed from
    /// the two.
    /// </summary>
    /// <remarks>
    /// The customer price used to be a stored column with a constraint of its own: the price the
    /// customer pays could not be a zero, and the unset state was an empty column. The column is
    /// gone with the copies (planning slice 2 of <c>docs/PLAN_PRICING_MODEL.md</c>), so the state
    /// the column carried is carried by the knobs now: a fuel with no cost and no margin has no
    /// customer price at all - and a grade with no price is not on the shelf.
    /// </remarks>
    [Fact]
    public async Task An_Unpriced_Fuel_Is_Allowed_And_Has_No_Customer_Price()
    {
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-unpriced", brandId);
        fuel.PumpPricePerLiter = 92.90m;

        await context.Set<FuelTypeEntity>().AddAsync(fuel);
        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync();

        var reloaded = await context.Set<FuelTypeEntity>()
            .IgnoreQueryFilters()
            .SingleAsync(f => f.Id == ToUuid("catalogue-unpriced"));
        reloaded.SupplierPricePerLiter.Should().BeNull("nobody has costed this fuel");
        reloaded.MarginUahPerLiter.Should().BeNull("nobody has set a margin");
        reloaded.FinalPerLiter().Should().BeNull("a fuel nobody has priced has no price, not a price of zero");

        await context.Set<FuelTypeEntity>().Where(f => f.Id == ToUuid("catalogue-unpriced")).ExecuteDeleteAsync();
    }

    /// <summary>
    /// A priced fuel is accepted, and the price it carries is the computed one: the cost plus the
    /// margin, never a stored copy that can disagree with them.
    /// </summary>
    [Fact]
    public async Task A_Priced_Fuel_Is_Accepted_And_Its_Price_Is_Computed()
    {
        await using var context = CreateContext();
        var brandId = await SeededBrand(context);

        var fuel = NewFuel("catalogue-priced", brandId);
        fuel.SupplierPricePerLiter = 45m;
        fuel.MarginUahPerLiter = 2m;

        context.Set<FuelTypeEntity>().Add(fuel);
        await context.SaveChangesAsync();

        (await context.Set<FuelTypeEntity>()
            .IgnoreQueryFilters()
            .SingleAsync(f => f.Id == ToUuid("catalogue-priced")))
            .FinalPerLiter()
            .Should().Be(47m, "the customer price is the cost plus the margin");

        await context.Set<FuelTypeEntity>().Where(f => f.Id == ToUuid("catalogue-priced")).ExecuteDeleteAsync();
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
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
    };

    private static FuelPriceHistory NewPriceHistoryRow(Guid brandId, string fuelTypeId) => new()
    {
        Id = Guid.NewGuid(),
        BrandId = brandId,
        FuelTypeId =   ToUuid(fuelTypeId),
        DataKind = "real",
        EffectiveFromUtc = TestClock.UtcNow,
        RecordedAtUtc = TestClock.UtcNow,
        Reason = "operator",
    };

    private static FuelTypeProductCode NewProductCode(string fuelTypeId, Guid brandId, string productCode) => new()
    {
        FuelTypeId =   ToUuid(fuelTypeId),
        BrandId = brandId,
        ProductCode = productCode,
    };
}
