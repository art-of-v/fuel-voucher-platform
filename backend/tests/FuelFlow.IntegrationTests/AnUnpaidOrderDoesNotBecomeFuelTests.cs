using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The acceptance criterion of the security finding S-1: an order whose status was written
/// directly - by a bug, a script, an operator who found a way round the endpoint - must not
/// receive fuel, because the money never arrived.
/// </summary>
[Collection("Integration Tests")]
public sealed class AnUnpaidOrderDoesNotBecomeFuelTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AnUnpaidOrderDoesNotBecomeFuelTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task An_Order_Whose_Status_Was_Written_Directly_Is_Not_Fulfilled()
    {
        // The order is put into PendingFulfillment the way the exploit did it: the status is
        // written, and nothing else. No payment row, because no money arrived.
        var orderId = await SeedOrderAsync(paid: false);

        var service = CreateService();
        await service.ProcessPendingOrdersAsync();

        using var verify = CreateContext();

        var fulfillments = await verify.Fulfillments.CountAsync(f => f.OrderId == orderId);
        fulfillments.Should().Be(0, "an unpaid order must not receive a voucher");

        var assignedVouchers = await verify.FuelVouchers.CountAsync(v => v.Status != VoucherStatus.Available);
        assignedVouchers.Should().Be(0, "the stock must stay where it is");

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().NotBe(OrderStatus.Fulfilled);
        order.FulfilledAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task A_Paid_Order_Is_Fulfilled()
    {
        // The other half of the same rule: the guard must not stop the ordinary path. This order
        // is paid for, so the vouchers are assigned as before.
        var orderId = await SeedOrderAsync(paid: true);

        var service = CreateService();
        await service.ProcessPendingOrdersAsync();

        using var verify = CreateContext();

        var fulfillments = await verify.Fulfillments.CountAsync(f => f.OrderId == orderId);
        fulfillments.Should().Be(1);

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.Fulfilled);
    }

    private async Task<Guid> SeedOrderAsync(bool paid)
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetDataAsync(seed);

        seed.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        seed.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500,
            // The status written directly - the shape the exploit had.
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    BrandId = BrandIds.ForSlug("okko"),
                    Brand = "okko",
                    FuelTypeId = ToUuid("okko-95"),
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = 2500,
                    LineTotal = 2500
                }
            }
        });

        if (paid)
        {
            seed.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Kind = PaymentKind.Capture,
                Provider = "monobank",
                ProviderInvoiceId = $"INV-{orderId:N}",
                AmountUah = 2500,
                AmountKopecks = 250000,
                Currency = "UAH",
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        // Stock to hand out. The unpaid case must leave it untouched - that is the whole point.
        for (var i = 0; i < 2; i++)
        {
            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 50,
                SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                VoucherNumber = $"OKKO-{orderId:N}-{i:D3}",
                QrPayload = $"payload-{orderId:N}-{i:D3}",
                Status = VoucherStatus.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await seed.SaveChangesAsync();
        return orderId;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private FulfillmentService CreateService()
    {
        var context = CreateContext();
        var monobankMock = new Mock<IMonobankClient>();
        monobankMock.Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        var refundHandler = new RefundOrderCommandHandler(
            context,
            new StubMonobankClientFactory(monobankMock.Object),
            new AuditEventService(context));

        return new FulfillmentService(
            context,
            NullLogger<FulfillmentService>.Instance,
            refundHandler,
            new RuntimeSettingsService(context),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build());
    }
}
