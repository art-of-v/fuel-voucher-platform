using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
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
/// The repair pass in <see cref="FulfillmentService"/> opens a transaction per order, and there was
/// a path through it that never closed that transaction.
/// </summary>
/// <remarks>
/// <para>
/// The path is the one where the pass finds a mismatch on an order OLDER than the reconciliation
/// grace window and deliberately leaves it alone for a human. It logged, notified, and continued -
/// and the <c>continue</c> jumped over the commit. There was no <c>finally</c> either, and the
/// <c>catch</c> only runs on an exception, so the transaction was neither committed nor rolled back
/// and the context still held it.
/// </para>
/// <para>
/// The consequence was not one skipped order. The next order's <c>BeginTransactionAsync</c> ran on
/// a context that already had a transaction, and the exception it raised was not caught anywhere in
/// the repair pass - so the WHOLE batch died. The fulfilment job that runs every five minutes then
/// did nothing for that run, and the order sweep that follows it in the same call never ran at all:
/// no customer waiting for a voucher got one.
/// </para>
/// <para>
/// <b>What this pins:</b> leaving an order alone is a decision about ONE order. It must not cost the
/// run the other orders, and it must not cost the run the open-order sweep that follows it.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FulfillmentTransactionScopeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FulfillmentTransactionScopeTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The user-visible consequence, stated as the assertion: a customer waiting for a voucher still
    /// gets one.
    /// </summary>
    /// <remarks>
    /// The two mismatched orders are the trigger - both are older than the grace window, so the pass
    /// leaves both alone. The third order is the witness: it is an ordinary pending order with stock
    /// behind it, and the open-order sweep that runs after the repair pass is what fulfils it.
    /// </remarks>
    [Fact]
    public async Task Leaving_Old_Mismatches_Alone_Does_Not_Stop_The_Run()
    {
        var userId = Guid.NewGuid();
        var leftAloneOrderId = Guid.NewGuid();
        var secondLeftAloneOrderId = Guid.NewGuid();
        var waitingOrderId = Guid.NewGuid();

        const string brand = "okko";
        const string fuelTypeId = "okko-95";
        const decimal liters = 50m;

        using (var seed = CreateContext())
        {
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

            // Two orders that will be left alone: a mismatch, and an age past the grace window.
            // `FulfilledAtUtc` is what the grace is measured from, so it has to be old.
            foreach (var orderId in new[] { leftAloneOrderId, secondLeftAloneOrderId })
            {
                var order = Order(orderId, userId, brand, fuelTypeId, liters, quantity: 1, unitPrice: 2500);
                order.Status = OrderStatus.Fulfilled;
                order.FulfilledAtUtc = DateTime.UtcNow.AddHours(-2);
                AddPaidOrder(seed, order);

                // Two delivery records for one line item: the mismatch. Both are left standing - the
                // point of the grace window is that an old order's fuel is not taken back.
                foreach (var tag in new[] { "OLD-A", "OLD-B" })
                {
                    var voucherId = Guid.NewGuid();
                    seed.FuelVouchers.Add(Assigned(voucherId, brand, fuelTypeId, liters, $"{tag}-{orderId:N}", userId, orderId));
                    seed.Fulfillments.Add(new Fulfillment
                    {
                        OrderId = orderId,
                        VoucherId = voucherId,
                        FulfilledAtUtc = DateTime.UtcNow.AddHours(-2)
                    });
                }
            }

            // The witness: an ordinary order waiting for its voucher, with stock behind it.
            var waiting = Order(waitingOrderId, userId, brand, fuelTypeId, liters, quantity: 1, unitPrice: 2500);
            waiting.Status = OrderStatus.PendingFulfillment;
            AddPaidOrder(seed, waiting);

            seed.FuelVouchers.Add(Available(Guid.NewGuid(), brand, fuelTypeId, liters, $"WAIT-{waitingOrderId:N}"));

            await seed.SaveChangesAsync();
        }

        var service = CreateService(CreateContext());
        // Before the fix this threw: the first left-alone order abandoned its transaction, and the
        // second order's BeginTransactionAsync found the context already in one.
        await service.ProcessPendingOrdersAsync();

        using var verify = CreateContext();

        var waitingOrder = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == waitingOrderId);
        waitingOrder.Status.Should().Be(OrderStatus.Fulfilled,
            "the run has to reach the open-order sweep after leaving the old mismatches alone");

        var waitingFulfillments = await verify.Fulfillments
            .AsNoTracking()
            .Where(f => f.OrderId == waitingOrderId)
            .ToListAsync();
        waitingFulfillments.Should().ContainSingle("the waiting customer got their voucher");

        // And the two left alone are still left alone: the grace window means an old order's fuel is
        // not taken back, and a fix to the transaction must not quietly change that.
        foreach (var orderId in new[] { leftAloneOrderId, secondLeftAloneOrderId })
        {
            var released = await verify.Fulfillments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(f => f.OrderId == orderId && f.ReleasedAtUtc != null)
                .ToListAsync();
            released.Should().BeEmpty("an order older than the grace window is left for a human");

            var standing = await verify.Fulfillments
                .AsNoTracking()
                .Where(f => f.OrderId == orderId)
                .ToListAsync();
            standing.Should().HaveCount(2, "both deliveries still stand");
        }
    }

    /// <summary>
    /// The narrower statement of the same defect: the context must not be left holding a transaction
    /// when the pass is done.
    /// </summary>
    /// <remarks>
    /// This one exists because the batch-level assertion above is satisfied by a run that throws and
    /// is caught further up. This asserts the state the defect actually leaves behind, and it is the
    /// thing that makes the next order's <c>BeginTransactionAsync</c> fail.
    /// </remarks>
    [Fact]
    public async Task The_Run_Leaves_No_Transaction_Open_On_The_Context()
    {
        var userId = Guid.NewGuid();
        var leftAloneOrderId = Guid.NewGuid();

        const string brand = "okko";
        const string fuelTypeId = "okko-95";
        const decimal liters = 50m;

        using (var seed = CreateContext())
        {
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

            var order = Order(leftAloneOrderId, userId, brand, fuelTypeId, liters, quantity: 1, unitPrice: 2500);
            order.Status = OrderStatus.Fulfilled;
            order.FulfilledAtUtc = DateTime.UtcNow.AddHours(-2);
            AddPaidOrder(seed, order);

            foreach (var tag in new[] { "TX-A", "TX-B" })
            {
                var voucherId = Guid.NewGuid();
                seed.FuelVouchers.Add(Assigned(voucherId, brand, fuelTypeId, liters, $"{tag}-{leftAloneOrderId:N}", userId, leftAloneOrderId));
                seed.Fulfillments.Add(new Fulfillment
                {
                    OrderId = leftAloneOrderId,
                    VoucherId = voucherId,
                    FulfilledAtUtc = DateTime.UtcNow.AddHours(-2)
                });
            }

            await seed.SaveChangesAsync();
        }

        var serviceContext = CreateContext();
        var service = CreateService(serviceContext);
        await service.ProcessPendingOrdersAsync();

        serviceContext.Database.CurrentTransaction.Should().BeNull(
            "every path through the repair pass closes the transaction it opened");

        await serviceContext.DisposeAsync();
    }

    private static Order Order(
        Guid id, Guid userId, string brand, string fuelTypeId, decimal liters, int quantity, decimal unitPrice) => new()
    {
        Id = id,
        UserId = userId,
        Price = quantity * unitPrice,
        Status = OrderStatus.PendingFulfillment,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
        LineItems = new List<OrderLineItem>
        {
            new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = id,
                BrandId = BrandIds.ForSlug(brand),
                Brand = brand,
                FuelTypeId = ToUuid(fuelTypeId),
                Liters = liters,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineTotal = quantity * unitPrice
            }
        }
    };

    private static FuelVoucher Assigned(
        Guid id, string brand, string fuelTypeId, decimal liters, string tag, Guid userId, Guid orderId) => new()
    {
        Id = id,
        BrandId = BrandIds.ForSlug(brand),
        Brand = brand,
        FuelTypeId = ToUuid(fuelTypeId),
        Liters = liters,
        SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        VoucherNumber = $"{tag}-{id:N}",
        QrPayload = $"{tag}-{id:N}",
        Status = VoucherStatus.Assigned,
        AssignedToUserId = userId,
        OrderId = orderId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static FuelVoucher Available(
        Guid id, string brand, string fuelTypeId, decimal liters, string tag) => new()
    {
        Id = id,
        BrandId = BrandIds.ForSlug(brand),
        Brand = brand,
        FuelTypeId = ToUuid(fuelTypeId),
        Liters = liters,
        SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        VoucherNumber = $"{tag}-{id:N}",
        QrPayload = $"{tag}-{id:N}",
        Status = VoucherStatus.Available,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private FulfillmentService CreateService(ApplicationDbContext context)
    {
        var monobank = new Mock<IMonobankClient>().Object;
        var refundHandler = new RefundOrderCommandHandler(
            context,
            new StubMonobankClientFactory(monobank),
            new AuditEventService(context));

        return new FulfillmentService(
            context,
            NullLogger<FulfillmentService>.Instance,
            refundHandler,
            new RuntimeSettingsService(context),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build());
    }
    /// <summary>
    /// Adds an order AND the money record of a paid order. Fulfilment asks the payment row
    /// whether the money arrived before it hands out a voucher, so a fixture that expects
    /// vouchers has to say the order was paid for.
    /// </summary>
    private static void AddPaidOrder(ApplicationDbContext seed, Order order)
    {
        seed.Orders.Add(order);
        seed.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Kind = PaymentKind.Capture,
            Provider = "monobank",
            ProviderInvoiceId = $"INV-{order.Id:N}",
            AmountUah = order.Price,
            AmountKopecks = (long)(order.Price * 100),
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }
}
