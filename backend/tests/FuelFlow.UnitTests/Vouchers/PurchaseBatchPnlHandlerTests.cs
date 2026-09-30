using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class PurchaseBatchPnlHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetImportBatchPnlQueryHandler _handler;

    public PurchaseBatchPnlHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _context.FuelTypes.Add(new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 22, DiscountPrice = 22, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        // Fuel priced at 22 UAH/L (cost 20 + margin 2, no pump).
        _context.FuelPackages.Add(new FuelPackage
        {
            Id = Guid.NewGuid().ToString(),
            StationId = "okko",
            FuelTypeId = "okko-dp",
            FuelName = "ДП ЄВРО",
            Liters = 100m,
            Price = 2200,
            OriginalPrice = 2200,
            SupplierPricePerLiter = 20m,
            MarginUahPerLiter = 2m,
            FinalPricePerLiter = 22m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();

        _handler = new GetImportBatchPnlQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private Guid SeedImport()
    {
        var id = Guid.NewGuid();
        _context.VoucherImports.Add(new VoucherImport { Id = id, FileName = "batch.pdf", Status = "Completed", StartedAtUtc = DateTime.UtcNow });
        return id;
    }

    private Guid SeedVoucher(Guid importId, decimal liters, VoucherStatus status, string fuelTypeId = "okko-dp", string provider = "OKKO")
    {
        var id = Guid.NewGuid();
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"V-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            ImportJobId = importId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        return id;
    }

    private void SeedBatchCost(Guid importId, decimal cost, string fuelTypeId = "okko-dp") =>
        _context.PurchaseBatches.Add(new PurchaseBatch
        {
            Id = Guid.NewGuid(),
            ImportJobId = importId,
            FuelTypeId = fuelTypeId,
            Provider = "OKKO",
            CostPerLiter = cost,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

    /// <summary>An order that bought <paramref name="voucherId"/> at <paramref name="unitPrice"/>, plus its line item + fulfillment.</summary>
    private void SeedSale(Guid voucherId, decimal liters, int unitPrice, OrderStatus status, string fuelTypeId = "okko-dp")
    {
        var orderId = Guid.NewGuid();
        _context.Orders.Add(new Order { Id = orderId, UserId = Guid.NewGuid(), Price = unitPrice, Status = status, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        _context.OrderLineItems.Add(new OrderLineItem { Id = Guid.NewGuid(), OrderId = orderId, Provider = "okko", FuelTypeId = fuelTypeId, Liters = liters, Quantity = 1, UnitPrice = unitPrice, LineTotal = unitPrice });
        _context.Fulfillments.Add(new Fulfillment { OrderId = orderId, VoucherId = voucherId, FulfilledAtUtc = DateTime.UtcNow });
    }

    [Fact]
    public async Task Pnl_RealizedAndUnrealized_AreComputed()
    {
        var import = SeedImport();
        var sold1 = SeedVoucher(import, 100m, VoucherStatus.Assigned);
        var sold2 = SeedVoucher(import, 100m, VoucherStatus.Used);
        SeedVoucher(import, 100m, VoucherStatus.Imported);   // remaining in stock
        SeedBatchCost(import, 20m);
        SeedSale(sold1, 100m, 2200, OrderStatus.Fulfilled);
        SeedSale(sold2, 100m, 2200, OrderStatus.Fulfilled);
        await _context.SaveChangesAsync();

        var row = (await _handler.HandleAsync(new GetImportBatchPnlQuery(import))).Should().ContainSingle().Subject;

        row.FuelTypeId.Should().Be("okko-dp");
        row.FuelTypeName.Should().Be("ДП ЄВРО");
        row.VouchersIn.Should().Be(3);
        row.LitersIn.Should().Be(300m);
        row.VouchersSold.Should().Be(2);
        row.LitersSold.Should().Be(200m);
        row.VouchersRemaining.Should().Be(1);
        row.LitersRemaining.Should().Be(100m);
        row.CostPerLiter.Should().Be(20m);
        row.RealizedRevenue.Should().Be(4400);
        row.RealizedCogs.Should().Be(4000m);        // 200 L × 20
        row.RealizedMargin.Should().Be(400m);        // 4400 − 4000
        row.AvgSalePricePerLiter.Should().Be(22m);   // 4400 / 200
        row.CurrentPricePerLiter.Should().Be(22m);
        row.UnrealizedMargin.Should().Be(200m);      // 100 L × (22 − 20)
        row.VouchersExpired.Should().Be(0);
        row.LitersExpired.Should().Be(0m);
        row.ExpiredLoss.Should().Be(0m);              // costed, nothing expired
        row.NetRealizedResult.Should().Be(400m);      // realized margin − 0 loss
    }

    [Fact]
    public async Task Pnl_RefundedOrCancelledOrders_DoNotCountAsRealized()
    {
        var import = SeedImport();
        var sold = SeedVoucher(import, 100m, VoucherStatus.Assigned);
        var refunded = SeedVoucher(import, 100m, VoucherStatus.Assigned);
        SeedBatchCost(import, 20m);
        SeedSale(sold, 100m, 2200, OrderStatus.Fulfilled);
        SeedSale(refunded, 100m, 2200, OrderStatus.Refunded);   // reversed → excluded
        await _context.SaveChangesAsync();

        var row = (await _handler.HandleAsync(new GetImportBatchPnlQuery(import))).Should().ContainSingle().Subject;

        row.VouchersSold.Should().Be(1);
        row.LitersSold.Should().Be(100m);
        row.RealizedRevenue.Should().Be(2200);
        row.RealizedCogs.Should().Be(2000m);
        row.RealizedMargin.Should().Be(200m);
    }

    [Fact]
    public async Task Pnl_UncostedBatch_LeavesCostDerivedFieldsNull_ButStillCountsRevenue()
    {
        var import = SeedImport();
        var sold = SeedVoucher(import, 100m, VoucherStatus.Used);
        SeedVoucher(import, 100m, VoucherStatus.Imported);
        SeedSale(sold, 100m, 2200, OrderStatus.Fulfilled);
        await _context.SaveChangesAsync();   // no PurchaseBatch → uncosted

        var row = (await _handler.HandleAsync(new GetImportBatchPnlQuery(import))).Should().ContainSingle().Subject;

        row.CostPerLiter.Should().BeNull();
        row.RealizedRevenue.Should().Be(2200);
        row.AvgSalePricePerLiter.Should().Be(22m);
        row.RealizedCogs.Should().BeNull();
        row.RealizedMargin.Should().BeNull();
        row.UnrealizedMargin.Should().BeNull();
    }

    [Fact]
    public async Task Pnl_NothingSold_RealizedIsZero_UnrealizedValuesRemainingStock()
    {
        var import = SeedImport();
        SeedVoucher(import, 100m, VoucherStatus.Imported);
        SeedVoucher(import, 50m, VoucherStatus.Available);
        SeedBatchCost(import, 20m);
        await _context.SaveChangesAsync();

        var row = (await _handler.HandleAsync(new GetImportBatchPnlQuery(import))).Should().ContainSingle().Subject;

        row.VouchersSold.Should().Be(0);
        row.RealizedRevenue.Should().Be(0);
        row.RealizedMargin.Should().Be(0m);          // costed but nothing sold
        row.AvgSalePricePerLiter.Should().BeNull();
        row.LitersRemaining.Should().Be(150m);
        row.UnrealizedMargin.Should().Be(300m);      // 150 L × (22 − 20)
    }

    [Fact]
    public async Task Pnl_ExpiredUnsoldStock_BooksLossAndNetsRealizedResult()
    {
        // Operator stock retired to Expired by the slice-4 job: its cost is a realised loss that nets
        // against the realised margin, and it stays out of the remaining (sellable) pool.
        var import = SeedImport();
        var sold = SeedVoucher(import, 100m, VoucherStatus.Used);
        SeedVoucher(import, 100m, VoucherStatus.Expired);    // lapsed unsold → loss
        SeedVoucher(import, 100m, VoucherStatus.Imported);   // still in stock
        SeedBatchCost(import, 20m);
        SeedSale(sold, 100m, 2200, OrderStatus.Fulfilled);
        await _context.SaveChangesAsync();

        var row = (await _handler.HandleAsync(new GetImportBatchPnlQuery(import))).Should().ContainSingle().Subject;

        row.VouchersSold.Should().Be(1);
        row.VouchersRemaining.Should().Be(1);          // Expired is neither sold nor remaining
        row.LitersRemaining.Should().Be(100m);
        row.VouchersExpired.Should().Be(1);
        row.LitersExpired.Should().Be(100m);
        row.RealizedMargin.Should().Be(200m);          // 2200 − 100 L × 20
        row.ExpiredLoss.Should().Be(2000m);            // 100 L × 20
        row.NetRealizedResult.Should().Be(-1800m);     // 200 − 2000
        row.UnrealizedMargin.Should().Be(200m);        // 100 L × (22 − 20), unaffected by the loss
    }

    [Fact]
    public async Task Pnl_ExpiredButUncosted_CountsVouchers_ButLeavesLossNull()
    {
        var import = SeedImport();
        SeedVoucher(import, 100m, VoucherStatus.Expired);
        await _context.SaveChangesAsync();   // no PurchaseBatch → uncosted

        var row = (await _handler.HandleAsync(new GetImportBatchPnlQuery(import))).Should().ContainSingle().Subject;

        row.VouchersExpired.Should().Be(1);
        row.LitersExpired.Should().Be(100m);
        row.ExpiredLoss.Should().BeNull();
        row.NetRealizedResult.Should().BeNull();
    }

    [Fact]
    public async Task Pnl_SoldThenExpiredVoucher_IsNotBookedAsLoss()
    {
        // A voucher that carries Expired status but was actually sold (non-reversed order) had its
        // revenue realised — its expiry is the customer's, not an operator loss, so it must stay out
        // of the expired-loss bucket and still count as sold.
        var import = SeedImport();
        var soldThenExpired = SeedVoucher(import, 100m, VoucherStatus.Expired);
        SeedBatchCost(import, 20m);
        SeedSale(soldThenExpired, 100m, 2200, OrderStatus.Fulfilled);
        await _context.SaveChangesAsync();

        var row = (await _handler.HandleAsync(new GetImportBatchPnlQuery(import))).Should().ContainSingle().Subject;

        row.VouchersSold.Should().Be(1);
        row.RealizedRevenue.Should().Be(2200);
        row.VouchersExpired.Should().Be(0);
        row.LitersExpired.Should().Be(0m);
        row.ExpiredLoss.Should().Be(0m);
        row.NetRealizedResult.Should().Be(200m);       // realized margin, no loss booked
    }

    [Fact]
    public async Task Pnl_EmptyImport_ReturnsEmpty()
    {
        var rows = await _handler.HandleAsync(new GetImportBatchPnlQuery(Guid.NewGuid()));
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Pnl_UsedVoucher_CountsAsSold()
    {
        // A redeemed (Used) voucher was still sold — it must contribute realized revenue and leave the stock pool.
        var import = SeedImport();
        var used = SeedVoucher(import, 100m, VoucherStatus.Used);
        SeedBatchCost(import, 20m);
        SeedSale(used, 100m, 2200, OrderStatus.Fulfilled);
        await _context.SaveChangesAsync();

        var row = (await _handler.HandleAsync(new GetImportBatchPnlQuery(import))).Should().ContainSingle().Subject;

        row.VouchersSold.Should().Be(1);
        row.VouchersRemaining.Should().Be(0);
        row.RealizedRevenue.Should().Be(2200);
    }
}
