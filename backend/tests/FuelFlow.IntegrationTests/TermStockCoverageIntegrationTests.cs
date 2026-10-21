using FluentAssertions;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The term ladder's stock question, answered for a whole cart in one query.
/// </summary>
/// <remarks>
/// <b>Why this runs against a real Postgres.</b> The answer is a GROUP BY with a MAX over the
/// voucher table, and the in-memory provider evaluates the expression in the client - which
/// proves the arithmetic and nothing about the SQL. If the translation fails, the failure is at
/// runtime, in the buy flow, on a cart that has a term on it.
/// </remarks>
[Collection("Integration Tests")]
public sealed class TermStockCoverageIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public TermStockCoverageIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_latest_expiry_is_answered_per_triple_in_one_query()
    {
        var fuelTypeId = Guid.NewGuid();
        var otherFuelTypeId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            // The seeded brands are used as they are: a brand of the same slug cannot be created
            // twice, and the point of the test is the stock question, not the catalogue.
            var okkoId = await context.Brands.Where(b => b.Slug == "okko").Select(b => b.Id).FirstAsync();
            var wogId = await context.Brands.Where(b => b.Slug == "wog").Select(b => b.Id).FirstAsync();

            context.FuelTypes.AddRange(
                FuelType(fuelTypeId, okkoId, "Diesel " + Guid.NewGuid().ToString("N")[..8]),
                FuelType(otherFuelTypeId, wogId, "Petrol " + Guid.NewGuid().ToString("N")[..8]));

            context.FuelVouchers.AddRange(
                Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 1, 1)),
                Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 6, 1)),
                Voucher("wog", otherFuelTypeId, 50m, new DateOnly(2027, 3, 1)));
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();

        var wanted = new[]
        {
            StockKey.For("okko", fuelTypeId, 50m),
            StockKey.For("wog", otherFuelTypeId, 50m)
        };

        var result = await TermStockCoverage.BestStockExpirationsAsync(verify, wanted, CancellationToken.None);

        result.Should().HaveCount(2);
        result[StockKey.For("okko", fuelTypeId, 50m)].Should().Be(new DateOnly(2027, 6, 1));
        result[StockKey.For("wog", otherFuelTypeId, 50m)].Should().Be(new DateOnly(2027, 3, 1));
    }


    private static FuelTypeEntity FuelType(Guid id, Guid brandId, string name) => new()
    {
        Id = id,
        Name = name,
        BrandId = brandId,
        BasePrice = 50m,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static FuelVoucher Voucher(string brand, Guid fuelTypeId, decimal liters, DateOnly supplierExpiration) => new()
    {
        Id = Guid.NewGuid(),
        Brand = brand,
        BrandId = BrandIds.ForSlug(brand),
        FuelTypeId = fuelTypeId,
        Liters = liters,
        VoucherNumber = Guid.NewGuid().ToString("N"),
        QrPayload = Guid.NewGuid().ToString("N"),
        SupplierExpirationDate = supplierExpiration,
        CustomerExpirationDate = supplierExpiration,
        Status = VoucherStatus.Available,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
