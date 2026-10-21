using FluentAssertions;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Orders;

/// <summary>
/// The term ladder asks one question per cart line: how far does the paper term of the stock
/// reach. It used to ask the database that question once per line.
/// </summary>
/// <remarks>
/// The batched answer is the same answer - the latest expiry among the available vouchers of a
/// fuel - computed by the database for every fuel of the cart at once. These pin the answer, and
/// pin the shape of it: one row per triple, the triple with no stock is simply absent, and a
/// fuel that is not in the cart is not in the result.
/// </remarks>
public sealed class TermStockCoverageTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public TermStockCoverageTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task The_latest_expiry_is_answered_per_triple()
    {
        var fuelTypeId = Guid.NewGuid();
        _context.FuelVouchers.AddRange(
            Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 1, 1)),
            Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 6, 1)),
            Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 3, 1)));
        await _context.SaveChangesAsync();

        var wanted = new[] { StockKey.For("okko", fuelTypeId, 50m) };

        var result = await TermStockCoverage.BestStockExpirationsAsync(_context, wanted, CancellationToken.None);

        result.Should().ContainSingle();
        result[StockKey.For("okko", fuelTypeId, 50m)].Should().Be(new DateOnly(2027, 6, 1));
    }

    [Fact]
    public async Task Each_triple_gets_its_own_answer()
    {
        var fuelTypeId = Guid.NewGuid();
        var otherFuelTypeId = Guid.NewGuid();
        _context.FuelVouchers.AddRange(
            Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 1, 1)),
            Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 6, 1)),
            Voucher("wog", otherFuelTypeId, 50m, new DateOnly(2027, 3, 1)));
        await _context.SaveChangesAsync();

        var wanted = new[]
        {
            StockKey.For("okko", fuelTypeId, 50m),
            StockKey.For("wog", otherFuelTypeId, 50m)
        };

        var result = await TermStockCoverage.BestStockExpirationsAsync(_context, wanted, CancellationToken.None);

        result.Should().HaveCount(2);
        result[StockKey.For("okko", fuelTypeId, 50m)].Should().Be(new DateOnly(2027, 6, 1));
        result[StockKey.For("wog", otherFuelTypeId, 50m)].Should().Be(new DateOnly(2027, 3, 1));
    }

    [Fact]
    public async Task A_triple_with_no_stock_is_absent_rather_than_zero()
    {
        var fuelTypeId = Guid.NewGuid();
        _context.FuelVouchers.Add(Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 1, 1)));
        await _context.SaveChangesAsync();

        var wanted = new[]
        {
            StockKey.For("okko", fuelTypeId, 50m),
            StockKey.For("okko", fuelTypeId, 95m)
        };

        var result = await TermStockCoverage.BestStockExpirationsAsync(_context, wanted, CancellationToken.None);

        result.Should().ContainSingle("the triple with no vouchers of that size is not in the result");
        result.ContainsKey(StockKey.For("okko", fuelTypeId, 95m)).Should().BeFalse();
    }

    [Fact]
    public async Task A_voucher_that_is_not_available_is_not_stock()
    {
        var fuelTypeId = Guid.NewGuid();
        _context.FuelVouchers.Add(
            Voucher("okko", fuelTypeId, 50m, new DateOnly(2027, 6, 1), VoucherStatus.Assigned));
        await _context.SaveChangesAsync();

        var wanted = new[] { StockKey.For("okko", fuelTypeId, 50m) };

        var result = await TermStockCoverage.BestStockExpirationsAsync(_context, wanted, CancellationToken.None);

        result.Should().BeEmpty("an assigned voucher is not stock the ladder can promise");
    }

    private static FuelVoucher Voucher(
        string brand,
        Guid fuelTypeId,
        decimal liters,
        DateOnly supplierExpiration,
        VoucherStatus status = VoucherStatus.Available) => new()
    {
        Id = Guid.NewGuid(),
        BrandId = BrandIds.ForSlug(brand),
        Brand = brand,
        FuelTypeId = fuelTypeId,
        Liters = liters,
        VoucherNumber = Guid.NewGuid().ToString("N"),
        QrPayload = Guid.NewGuid().ToString("N"),
        SupplierExpirationDate = supplierExpiration,
        CustomerExpirationDate = supplierExpiration,
        Status = status,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };
}
