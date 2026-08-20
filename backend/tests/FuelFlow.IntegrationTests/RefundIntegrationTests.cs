using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the manual refund lifecycle against a real PostgreSQL container:
/// admin-initiated refund -> Monobank cancel call -> RefundStatusSyncService
/// confirmation/failure -> order status flip. The automatic refund path is
/// covered by FulfillmentConcurrencyIntegrationTests.
/// </summary>
[Collection("Integration Tests")]
public sealed class RefundIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RefundIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ManualRefund_WhenMonobankConfirms_ShouldCompleteRefundAndFlipOrderToRefunded()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            seed.Orders.Add(CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 2, unitPrice: 1000));
        });

        var monobankMock = CreateMonobankMock(out var cancelledAmount);
        monobankMock.Setup(m => m.GetInvoiceStatusAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "success",
                        Amount = 200000,
                        ExtRef = orderId.ToString(),
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        RefundOrderResult result;
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, monobankMock.Object, new ProviderEventService(context));
            result = await handler.HandleAsync(new RefundOrderCommand
            {
                OrderId = orderId,
                IsAutomatic = false,
                ChangedByUserName = "admin-test"
            });
        }

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(200000);
        cancelledAmount.Value.Should().Be(200000);

        // While the refund is in flight the order must keep its fulfillment-derived status.
        using (var mid = CreateContext())
        {
            var order = await mid.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.PendingFulfillment);

            var refund = await mid.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
            refund.Status.Should().Be(RefundStatus.Processing);
            refund.IsAutomatic.Should().BeFalse();
            refund.CreatedByUserName.Should().Be("admin-test");
        }

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, monobankMock.Object, NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
            refund.Status.Should().Be(RefundStatus.Completed);
            refund.MonobankStatus.Should().Be("success");

            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.Refunded);
        }
    }

    [Fact]
    public async Task ManualRefund_WithDeliveredVouchers_ShouldFlipOrderToPartiallyRefunded()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));

            var order = CreateOrder(orderId, userId, invoiceId, OrderStatus.PartiallyFulfilled,
                quantity: 3, unitPrice: 520);
            seed.Orders.Add(order);

            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "OKKO",
                FuelTypeId = "okko-dp",
                Liters = 10m,
                ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                VoucherNumber = $"MR-{orderId:N}-000",
                QrPayload = $"payload-{orderId:N}-000",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            seed.FuelVouchers.Add(voucher);
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucher.Id,
                FulfilledAtUtc = DateTime.UtcNow.AddHours(-1)
            });
        });

        var monobankMock = CreateMonobankMock(out _);
        monobankMock.Setup(m => m.GetInvoiceStatusAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "success",
                        ExtRef = orderId.ToString(),
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        RefundOrderResult result;
        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, monobankMock.Object, new ProviderEventService(context));
            result = await handler.HandleAsync(new RefundOrderCommand { OrderId = orderId });
        }

        // Only the two undelivered vouchers are refundable: 2 * 520 UAH.
        result.AmountKopecks.Should().Be(104000);

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, monobankMock.Object, NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
            refund.Status.Should().Be(RefundStatus.Completed);
            refund.Amount.Should().Be(104000);

            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.PartiallyRefunded);
        }
    }

    [Fact]
    public async Task ManualRefund_WhenMonobankReportsFailure_ShouldFailRefundAndKeepOrderStatus()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var invoiceId = $"test-invoice-{orderId:N}";

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            seed.Orders.Add(CreateOrder(orderId, userId, invoiceId, OrderStatus.PendingFulfillment,
                quantity: 1, unitPrice: 1000));
        });

        var monobankMock = CreateMonobankMock(out _);
        monobankMock.Setup(m => m.GetInvoiceStatusAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "failure",
                        ExtRef = orderId.ToString(),
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        using (var context = CreateContext())
        {
            var handler = new RefundOrderCommandHandler(context, monobankMock.Object, new ProviderEventService(context));
            await handler.HandleAsync(new RefundOrderCommand { OrderId = orderId });
        }

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, monobankMock.Object, NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
            refund.Status.Should().Be(RefundStatus.Failed);
            refund.MonobankStatus.Should().Be("failure");
            refund.ErrorMessage.Should().Be("Monobank reported refund failure");

            // A failed refund must never leave the order in a refunded state.
            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.PendingFulfillment);
        }
    }

    [Fact]
    public async Task StaleRefund_OlderThan24h_ShouldTimeOutAndRevertPrematureOrderStatus()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));

            // Legacy inconsistent state: order already shows refunded while its
            // refund is still Processing (and has been for over a day).
            var order = CreateOrder(orderId, userId, $"test-invoice-{orderId:N}", OrderStatus.PartiallyRefunded,
                quantity: 2, unitPrice: 1000);
            seed.Orders.Add(order);

            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "OKKO",
                FuelTypeId = "okko-dp",
                Liters = 10m,
                ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                VoucherNumber = $"SR-{orderId:N}-000",
                QrPayload = $"payload-{orderId:N}-000",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            seed.FuelVouchers.Add(voucher);
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucher.Id,
                FulfilledAtUtc = DateTime.UtcNow.AddDays(-2)
            });

            seed.Refunds.Add(new Refund
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                UserId = userId,
                Amount = 100000,
                InvoiceId = order.MonobankInvoiceId!,
                ExtRef = orderId.ToString(),
                Status = RefundStatus.Processing,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-25),
                UpdatedAtUtc = DateTime.UtcNow.AddHours(-25)
            });
        });

        var monobankMock = CreateMonobankMock(out _);

        using (var syncContext = CreateContext())
        {
            var syncService = new RefundStatusSyncService(
                syncContext, monobankMock.Object, NullLogger<RefundStatusSyncService>.Instance);
            await syncService.SyncPendingRefundsAsync();
        }

        using (var verify = CreateContext())
        {
            var refund = await verify.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
            refund.Status.Should().Be(RefundStatus.Failed);
            refund.ErrorMessage.Should().Contain("Timed out");

            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.PartiallyFulfilled);
        }
    }

    private static User CreateUser(Guid userId) => new()
    {
        Id = userId,
        PhoneNumber = $"+38{userId:N}"[..20],
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Order CreateOrder(
        Guid orderId,
        Guid userId,
        string invoiceId,
        OrderStatus status,
        int quantity,
        int unitPrice) => new()
    {
        Id = orderId,
        UserId = userId,
        Price = quantity * unitPrice,
        Status = status,
        MonobankInvoiceId = invoiceId,
        MonobankStatus = MonobankStatus.Success,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
        LineItems = new List<OrderLineItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Provider = "OKKO",
                FuelTypeId = "okko-dp",
                Liters = 10m,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineTotal = quantity * unitPrice
            }
        }
    };

    private static Mock<IMonobankClient> CreateMonobankMock(out AmountBox cancelledAmount)
    {
        var captured = new AmountBox();
        var mock = new Mock<IMonobankClient>();
        mock.Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, long, string, CancellationToken>((_, amount, _, _) => captured.Value = amount)
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });
        cancelledAmount = captured;
        return mock;
    }

    /// <summary>Reference holder so the captured cancel amount outlives the mock setup.</summary>
    private sealed class AmountBox
    {
        public long Value { get; set; }
    }

    private async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        // The sync service scans ALL refunds, so leftover state from a previous test in
        // this shared class-fixture database would interfere. Keep each test hermetic.
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

        seed(context);
        await context.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
