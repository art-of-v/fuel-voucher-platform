using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Features.Report.GetReport;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Report;

public sealed class GetReportQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public GetReportQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.AddMissing();
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
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
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
            Brand = "okko",
            FuelTypeId = ToUuid("okko-dp"),
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
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            Quantity = 1,
            UnitPrice = 9999,
            LineTotal = 9999
        });

        _context.Orders.AddRange(paidOrder, pendingOrder, excludedOrder);

        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = ToUuid("okko-95"), Name = "A-95", BrandId = BrandIds.Okko, BasePrice = 100, DiscountPrice = 100, CreatedAtUtc = now, UpdatedAtUtc = now },
            new FuelTypeEntity { Id = ToUuid("okko-dp"), Name = "DP", BrandId = BrandIds.Okko, BasePrice = 100, DiscountPrice = 100, CreatedAtUtc = now, UpdatedAtUtc = now });

        // Seeded as offers plus volumes: the report resolves its margin through
        // (Brand, fuel, litres) -> fuel_offers, so per-litre figures belong on the offer.
        _context.SeedPackage("okko-95-50", BrandIds.Okko, "okko-95", 50m, 5100m,
            supplierPricePerLiter: 100m, marginUahPerLiter: 2m, finalPricePerLiter: 102m,
            createdAtUtc: now, updatedAtUtc: now);
        _context.SeedPackage("okko-dp-20", BrandIds.Okko, "okko-dp", 20m, 2000m,
            supplierPricePerLiter: 100m, marginUahPerLiter: 1.5m, finalPricePerLiter: 101.5m,
            createdAtUtc: now, updatedAtUtc: now);

        var voucher95 = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
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
            Brand = "okko",
            FuelTypeId = ToUuid("okko-dp"),
            Liters = 20m,
            SupplierExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
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
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 50m,
                SupplierExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
                CustomerExpirationDate = DateOnly.FromDateTime(now.AddMonths(1)),
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

        // The report reads refund *payments* now, so it is seeded as one. A refund and its payment
        // are the same identity since the stage-2 migration, which is why the id is shared.
        _context.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = pendingOrder.Id,
            Kind = PaymentKind.Refund,
            Provider = "monobank",
            ProviderInvoiceId = "inv-1",
            AmountUah = 1000.00m,
            AmountKopecks = 100000,
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = now,
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
        payment.Brand.Should().Be("okko");
        payment.FuelType.Should().Be(ToUuid("okko-95"));
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

        var redemption = response.Redemptions.Single(r => r.Brand == "okko" && r.FuelType == ToUuid("okko-95"));
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
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
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
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
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
