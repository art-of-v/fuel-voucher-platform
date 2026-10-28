using System.Text.Json;
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The fast path of fulfilment is the ORDER_CREATED outbox event: the paid webhook writes it, the
/// dispatcher hands it to <see cref="FulfillmentService.ProcessOrderCreatedEventAsync"/>, and the
/// order is fulfilled seconds after the money lands.
/// </summary>
/// <remarks>
/// <para>
/// That path never ran. The producer serialises the payload from an anonymous object, so its keys are
/// camelCase (<c>orderId</c>), and the consumer read it with <see cref="JsonSerializer"/>'s default
/// options, which are case-sensitive. <c>orderId</c> therefore bound to nothing, the record's
/// non-nullable <see cref="Guid"/> property stayed at <see cref="Guid.Empty"/>, and the handler logged
/// "Order 00000000-0000-0000-0000-000000000000 not found" and marked the event processed - on every
/// paid order, including the live sale of 2026-10-28.
/// </para>
/// <para>
/// Nothing failed loudly because <see cref="FulfillmentService.ProcessPendingOrdersAsync"/> sweeps
/// open orders by status afterwards, so the customer still got a voucher a few seconds later. This
/// suite is what makes the difference between "the event works" and "the sweep rescues it" visible:
/// it calls the event handler on its own, so only the event path can satisfy the assertions.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class OrderCreatedEventFulfilsItsOrderTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OrderCreatedEventFulfilsItsOrderTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_order_created_event_fulfils_the_order_it_names()
    {
        var (orderId, outboxEventId) = await SeedPaidOrderWithEventsAsync();

        var service = CreateService();
        using (var act = CreateContext())
        {
            var outboxEvent = await act.OutboxEvents.AsNoTracking().FirstAsync(e => e.Id == outboxEventId);
            await service.ProcessOrderCreatedEventAsync(outboxEvent, CancellationToken.None);
        }

        using var verify = CreateContext();

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.Fulfilled, "the event names the order, so it must be fulfilled");
        order.FulfilledAtUtc.Should().NotBeNull();

        (await verify.Fulfillments.CountAsync(f => f.OrderId == orderId)).Should().Be(1);

        var voucher = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.VoucherNumber == $"EVENT-{orderId:N}");
        voucher.Status.Should().Be(VoucherStatus.Assigned);

        var processed = await verify.OutboxEvents.AsNoTracking().FirstAsync(e => e.Id == outboxEventId);
        processed.Processed.Should().BeTrue();
        processed.ProcessedAtUtc.Should().NotBeNull();
    }

    private async Task<(Guid OrderId, Guid OutboxEventId)> SeedPaidOrderWithEventsAsync()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var outboxEventId = Guid.NewGuid();

        using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        await ResetDataAsync(seed);

        seed.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        seed.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
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
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow
        });

        seed.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
            VoucherNumber = $"EVENT-{orderId:N}",
            QrPayload = $"payload-{orderId:N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        // The payload exactly as ProcessMonobankWebhookCommandHandler writes it.
        seed.OutboxEvents.Add(new OutboxEvent
        {
            Id = outboxEventId,
            EventType = OutboxEventType.OrderCreated,
            Payload = JsonSerializer.Serialize(new
            {
                orderId,
                userId,
                brand = "okko",
                fuelType = ToUuid("okko-95"),
                liters = 50m,
                quantity = 1
            }),
            Processed = false,
            CreatedAtUtc = TestClock.UtcNow
        });

        await seed.SaveChangesAsync();
        return (orderId, outboxEventId);
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
        monobankMock
            .Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()))
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
