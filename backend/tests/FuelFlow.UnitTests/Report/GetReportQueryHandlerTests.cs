using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Report.GetReport;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Report;

public sealed class GetReportQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public GetReportQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetReport_ShouldAggregatePaymentsAndRedemptions()
    {
        var now = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var userId = Guid.NewGuid();

        _context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = "+380991111111",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true
        });

        var paidOrder = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 5100,
            Status = OrderStatus.Paid,
            MonobankStatus = MonobankStatus.Success,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        paidOrder.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = paidOrder.Id,
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 50m,
            Quantity = 1,
            UnitPrice = 5100,
            LineTotal = 5100
        });

        var pendingOrder = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 2000,
            Status = OrderStatus.PartiallyFulfilled,
            MonobankStatus = MonobankStatus.Success,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        pendingOrder.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = pendingOrder.Id,
            Provider = "okko",
            FuelTypeId = "okko-dp",
            Liters = 20m,
            Quantity = 2,
            UnitPrice = 1000,
            LineTotal = 2000
        });

        var excludedOrder = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 9999,
            Status = OrderStatus.PendingPayment,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        excludedOrder.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = excludedOrder.Id,
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 50m,
            Quantity = 1,
            UnitPrice = 9999,
            LineTotal = 9999
        });

        _context.Orders.AddRange(paidOrder, pendingOrder, excludedOrder);

        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 100, DiscountPrice = 100, CreatedAtUtc = now, UpdatedAtUtc = now },
            new FuelTypeEntity { Id = "okko-dp", Name = "DP", StationId = "okko", BasePrice = 100, DiscountPrice = 100, CreatedAtUtc = now, UpdatedAtUtc = now });

        _context.FuelPackages.AddRange(
            new FuelPackage
            {
                Id = "okko-95-50",
                StationId = "okko",
                FuelTypeId = "okko-95",
                FuelName = "A-95",
                Liters = 50m,
                Price = 5100,
                OriginalPrice = 5000,
                SupplierPricePerLiter = 100m,
                MarginUahPerLiter = 2m,
                FinalPricePerLiter = 102m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new FuelPackage
            {
                Id = "okko-dp-20",
                StationId = "okko",
                FuelTypeId = "okko-dp",
                FuelName = "DP",
                Liters = 20m,
                Price = 2000,
                OriginalPrice = 1970,
                SupplierPricePerLiter = 100m,
                MarginUahPerLiter = 1.5m,
                FinalPricePerLiter = 101.5m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        var voucher95 = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 50m,
            ExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
            VoucherNumber = "V-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Used,
            AssignedToUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var voucherDp = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "okko",
            FuelTypeId = "okko-dp",
            Liters = 20m,
            ExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
            VoucherNumber = "V-2",
            QrPayload = "payload-2",
            Status = VoucherStatus.Used,
            AssignedToUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _context.FuelVouchers.AddRange(
            voucher95,
            voucherDp,
            new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "okko",
                FuelTypeId = "okko-95",
                Liters = 50m,
                ExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
                VoucherNumber = "V-3",
                QrPayload = "payload-3",
                Status = VoucherStatus.Available,
                AssignedToUserId = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        // Ledger setup: paidOrder fully delivered, pendingOrder 1 of 2 delivered and
        // the undelivered unit refunded (1 × 1000 UAH = 100000 kopecks).
        _context.Fulfillments.AddRange(
            new Fulfillment { OrderId = paidOrder.Id, VoucherId = voucher95.Id, FulfilledAtUtc = now },
            new Fulfillment { OrderId = pendingOrder.Id, VoucherId = voucherDp.Id, FulfilledAtUtc = now });

        _context.Refunds.Add(new Refund
        {
            Id = Guid.NewGuid(),
            OrderId = pendingOrder.Id,
            UserId = userId,
            Amount = 100000,
            InvoiceId = "inv-1",
            ExtRef = "ext-1",
            Status = RefundStatus.Completed,
            MonobankStatus = "success",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await _context.SaveChangesAsync();

        var handler = new GetReportQueryHandler(_context);
        var response = await handler.HandleAsync(new GetReportQuery(userId), CancellationToken.None);

        response.Should().NotBeNull();
        response.Payments.Should().HaveCount(2);
        response.Redemptions.Should().HaveCount(2);

        response.Summary.TotalOrders.Should().Be(2);
        response.Summary.VouchersPurchased.Should().Be(3);
        response.Summary.VouchersUsed.Should().Be(2);
        // Earned margin only: 50L×2 UAH (paidOrder) + 20L×1.5 UAH × 1 of 2 delivered (pendingOrder).
        response.Summary.TotalSpent.Should().Be(130);
        response.Summary.TotalLitersPurchased.Should().Be(90m);
        response.Summary.TotalLitersUsed.Should().Be(70m);
        // Ledger: received 7100 UAH, delivered value 5100 + 1000 UAH, refunded 1000 UAH.
        response.Summary.TotalReceivedKopecks.Should().Be(710000);
        response.Summary.TotalFulfilledValueKopecks.Should().Be(610000);
        response.Summary.TotalRefundedKopecks.Should().Be(100000);

        var payment = response.Payments.Single(p => p.OrderId == paidOrder.Id);
        payment.Amount.Should().Be(5100);
        payment.Status.Should().Be("Paid");
        payment.Provider.Should().Be("okko");
        payment.FuelType.Should().Be("okko-95");
        payment.FuelName.Should().Be("A-95");
        payment.Liters.Should().Be(50m);
        payment.Quantity.Should().Be(1);
        payment.FulfilledValueKopecks.Should().Be(510000);
        payment.RefundedKopecks.Should().Be(0);

        var refundedPayment = response.Payments.Single(p => p.OrderId == pendingOrder.Id);
        refundedPayment.Status.Should().Be("PartiallyFulfilled");
        refundedPayment.FulfilledValueKopecks.Should().Be(100000);
        refundedPayment.RefundedKopecks.Should().Be(100000);
        refundedPayment.RefundStatus.Should().Be("Completed");

        var redemption = response.Redemptions.Single(r => r.Provider == "okko" && r.FuelType == "okko-95");
        redemption.FuelName.Should().Be("A-95");
        redemption.Liters.Should().Be(50m);

        response.MonthlyBreakdown.Should().ContainSingle();
        var monthly = response.MonthlyBreakdown.Single();
        monthly.Month.Should().Be("2026-07");
        monthly.TotalSpent.Should().Be(130);
        monthly.VouchersPurchased.Should().Be(3);
        monthly.VouchersUsed.Should().Be(2);
        monthly.TotalLitersPurchased.Should().Be(90m);
        monthly.TotalLitersUsed.Should().Be(70m);
        monthly.TotalRefundedKopecks.Should().Be(100000);
    }

    [Fact]
    public async Task GetReport_ShouldReturnEmptyReport_WhenNoData()
    {
        var handler = new GetReportQueryHandler(_context);
        var response = await handler.HandleAsync(new GetReportQuery(null), CancellationToken.None);

        response.Period.From.Should().BeNull();
        response.Period.To.Should().BeNull();
        response.Payments.Should().BeEmpty();
        response.Redemptions.Should().BeEmpty();
        response.MonthlyBreakdown.Should().BeEmpty();
        response.Summary.TotalOrders.Should().Be(0);
        response.Summary.VouchersPurchased.Should().Be(0);
        response.Summary.VouchersUsed.Should().Be(0);
        response.Summary.TotalSpent.Should().Be(0);
        response.Summary.TotalLitersPurchased.Should().Be(0m);
        response.Summary.TotalLitersUsed.Should().Be(0m);
    }

    [Fact]
    public async Task GetReport_ShouldFilterByUserId()
    {
        var now = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var orderA = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userA,
            Price = 1000,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        orderA.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderA.Id,
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 10m,
            Quantity = 1,
            UnitPrice = 1000,
            LineTotal = 1000
        });

        var orderB = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userB,
            Price = 2000,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        orderB.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderB.Id,
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 10m,
            Quantity = 1,
            UnitPrice = 2000,
            LineTotal = 2000
        });

        _context.Orders.AddRange(orderA, orderB);
        await _context.SaveChangesAsync();

        var handler = new GetReportQueryHandler(_context);
        var response = await handler.HandleAsync(new GetReportQuery(userA), CancellationToken.None);

        response.Payments.Should().ContainSingle();
        response.Payments[0].OrderId.Should().Be(orderA.Id);
        response.Payments[0].Amount.Should().Be(1000);
        response.Summary.TotalOrders.Should().Be(1);
        response.Redemptions.Should().BeEmpty();
    }
}
