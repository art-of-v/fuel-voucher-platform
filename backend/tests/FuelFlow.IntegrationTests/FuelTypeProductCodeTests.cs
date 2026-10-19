using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_type_product_codes</c> — one QR product code, one grade, per network. Planning #349,
/// step 3b.
/// </summary>
/// <remarks>
/// <para>
/// The table exists because the QR product code is the one signal in a parsed voucher that is not a
/// name, and resolving a fuel by it is exact. These tests hold the constraint that makes the exactness
/// true: two grades claiming one code at one network is what would send every voucher of that paper to
/// whichever row the database returned first, and a voucher priced against the wrong fuel is real money.
/// </para>
/// <para>
/// The per-network scoping is the other half and the one that is easy to get wrong: OKKO's 9018 and
/// KLO's own codes share a numbering space we cannot prove they do not, so the unique index leads with
/// the network while the code alone stays indexed for a cross-network question.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FuelTypeProductCodeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelTypeProductCodeTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static FuelTypeProductCode Code(string fuelTypeId, Guid brandId, string productCode)
        => new() { FuelTypeId = fuelTypeId, BrandId = brandId, ProductCode = productCode };

    [Fact]
    public async Task TwoFuelsAtOneNetwork_CannotClaimOneProductCode()
    {
        await using var context = CreateContext();

        context.FuelTypeProductCodes.Add(Code("okko-dp", BrandIds.Okko, "9018"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.FuelTypeProductCodes.Add(Code("okko-p95", BrandIds.Okko, "9018"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "the voucher's QR is the only exact signal in a parsed voucher, and two answers to one code is no answer");

        await CleanupAsync(BrandIds.Okko, "9018");
    }

    [Fact]
    public async Task OneFuel_CanCarrySeveralCodes_AndOneCode_FitsSeveralFuels()
    {
        // The cardinality the table exists for, in both directions: diesel is 9018 and 9518, and a
        // column could have held only one of them.
        await using var context = CreateContext();

        context.FuelTypeProductCodes.AddRange(
            Code("okko-dp", BrandIds.Okko, "9018"),
            Code("okko-dp", BrandIds.Okko, "9518"),
            Code("okko-95", BrandIds.Okko, "9015"),
            Code("okko-95", BrandIds.Okko, "9016"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        var forDiesel = await context.FuelTypeProductCodes
            .Where(c => c.FuelTypeId == "okko-dp")
            .Select(c => c.ProductCode)
            .ToListAsync();
        forDiesel.Should().BeEquivalentTo(["9018", "9518"]);

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM fuel_type_product_codes WHERE fuel_type_id IN ('okko-dp', 'okko-95')");
    }

    [Fact]
    public async Task TheSameCodeAtTwoNetworks_IsNotADuplicate()
    {
        // A code identifies a grade within a network. Refusing it across networks would be wrong
        // (they may legitimately overlap) and permitting it globally would silently price a WOG
        // voucher against an OKKO fuel if they ever did.
        //
        // The two fuels are real ones, at two real networks. They have to be: a product code points
        // at a fuel, and that is a foreign key now, so a row naming a fuel that does not exist is
        // refused by the database before the unique index ever gets to say anything about codes.
        await using var context = CreateContext();

        context.FuelTypeProductCodes.Add(Code("okko-dp", BrandIds.Okko, "7701"));
        context.FuelTypeProductCodes.Add(Code("wog-dp", BrandIds.Wog, "7701"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();

        await CleanupAsync(BrandIds.Okko, "7701");
        await CleanupAsync(BrandIds.Wog, "7701");
    }

    [Fact]
    public async Task ACodeIsAtMostOnceForOneFuel()
    {
        // The primary key. The unique index above only catches two *different* fuels claiming one code.
        await using var context = CreateContext();

        context.FuelTypeProductCodes.Add(Code("okko-gas", BrandIds.Okko, "9019"));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.FuelTypeProductCodes.Add(Code("okko-gas", BrandIds.Okko, "9019"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await CleanupAsync(BrandIds.Okko, "9019");
    }

    [Fact]
    public async Task ABlankCode_IsRefused()
    {
        // Otherwise a lookup that matches "" claims every voucher whose QR carried no code — the one
        // case where the caller has nothing to resolve.
        await using var context = CreateContext();

        context.FuelTypeProductCodes.Add(Code("okko-gas", BrandIds.Okko, "   "));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AnOverlongCode_IsRefused()
    {
        // 32 is generous for a printed number; a 33-character "product code" is a paste of the wrong
        // field, and the column is indexed and unique per network.
        await using var context = CreateContext();

        context.FuelTypeProductCodes.Add(Code("okko-gas", BrandIds.Okko, new string('9', 33)));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TheTableStartsEmpty()
    {
        // Documents the migration's decision rather than its effect: nothing was backfilled, because a
        // code inferred from a category is a guess, and this table decides what a voucher is worth.
        await using var context = CreateContext();

        var rows = await context.FuelTypeProductCodes.IgnoreQueryFilters().CountAsync();

        rows.Should().Be(0, "the codes are what paper in the wild carries, and only paper knows them");
    }

    private async Task CleanupAsync(Guid brandId, string productCode)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM fuel_type_product_codes WHERE brand_id = {0} AND product_code = {1}",
            brandId, productCode);
    }
}
