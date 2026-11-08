using FluentAssertions;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
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
/// The QA account buys from the real shop. <see cref="User.IsQaAccount"/> still routes payments to
/// the sandbox and is what the OFF switch revokes, but it no longer chooses a stock pool: a QA
/// account is fulfilled from <c>data_kind = 'real'</c> like any customer.
/// </summary>
/// <remarks>
/// The pool filter that remains is the one that protects real customers: a rehearsal voucher
/// (<c>data_kind = 'qa'</c>) is never handed out, to ANY account. Enforced at the candidate SELECT
/// in fulfilment and re-asserted by the atomic claim UPDATE, so a row that changes kind between
/// the two can never cross.
/// </remarks>
[Collection("Integration Tests")]
public sealed class QaAccountBuysFromRealStockIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const string Brand = "okko";
    private const string FuelTypeId = "okko-95";
    private const decimal Liters = 10m;

    private readonly TestDatabaseFixture _fixture;

    public QaAccountBuysFromRealStockIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fulfillment_HandsAQaAccountRealStock_LikeAnyCustomer()
    {
        var qaUserId = Guid.NewGuid();
        var realUserId = Guid.NewGuid();
        var qaOrderId = Guid.NewGuid();
        var realOrderId = Guid.NewGuid();
        var firstVoucherId = Guid.NewGuid();
        var secondVoucherId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(MakeUser(qaUserId, isQaAccount: true));
            seed.Users.Add(MakeUser(realUserId, isQaAccount: false));

            AddPaidOrder(seed, MakeOrder(qaOrderId, qaUserId));
            AddPaidOrder(seed, MakeOrder(realOrderId, realUserId));

            // Two interchangeable real vouchers — same brand, same fuel, same nominal — so neither
            // order has a row it is entitled to. What is under test is that both are claimable.
            seed.FuelVouchers.Add(MakeVoucher(firstVoucherId, "real-a", isTestData: false));
            seed.FuelVouchers.Add(MakeVoucher(secondVoucherId, "real-b", isTestData: false));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await CreateService(ctx).ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();

        var qaOrder = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == qaOrderId);
        var realOrder = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == realOrderId);
        qaOrder.Status.Should().Be(OrderStatus.Fulfilled);
        realOrder.Status.Should().Be(OrderStatus.Fulfilled);

        // Both got real stock. The flag changes where the money goes, not what is on the shelf — and
        // which of the two rows each order won is the fulfilment's choice, not a property under test:
        // both vouchers carry the same stock key, so the pair is asserted as a pair, not by identity.
        var claimed = await verify.FuelVouchers.AsNoTracking()
            .Where(v => v.Id == firstVoucherId || v.Id == secondVoucherId)
            .ToListAsync();

        claimed.Should().HaveCount(2);
        claimed.Should().OnlyContain(v => v.Status == VoucherStatus.Assigned);
        claimed.Select(v => v.AssignedToUserId).Should().BeEquivalentTo(new[] { qaUserId, realUserId });
    }

    [Fact]
    public async Task Fulfillment_WillNotHandRehearsalStock_ToAQaAccount()
    {
        var qaUserId = Guid.NewGuid();
        var qaOrderId = Guid.NewGuid();
        var testVoucherId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(MakeUser(qaUserId, isQaAccount: true));
            AddPaidOrder(seed, MakeOrder(qaOrderId, qaUserId));

            // Only a rehearsal voucher exists. It is not sellable to anyone, including QA.
            seed.FuelVouchers.Add(MakeVoucher(testVoucherId, "rehearsal", isTestData: true));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await CreateService(ctx).ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();

        var qaOrder = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == qaOrderId);
        qaOrder.Status.Should().Be(OrderStatus.PendingFulfillment);

        var testVoucher = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == testVoucherId);
        testVoucher.Status.Should().Be(VoucherStatus.Available);
        testVoucher.AssignedToUserId.Should().BeNull();

        (await verify.Fulfillments.AsNoTracking().CountAsync(f => f.OrderId == qaOrderId))
            .Should().Be(0);
    }

    [Fact]
    public async Task TheAtomicClaim_AcceptsRealStockForAQaAccount_AndRefusesRehearsalStock()
    {
        var qaUserId = Guid.NewGuid();
        var realUserId = Guid.NewGuid();
        var qaOrderId = Guid.NewGuid();
        var realOrderId = Guid.NewGuid();
        var realId = Guid.NewGuid();
        var testForQaId = Guid.NewGuid();
        var testForRealId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(MakeUser(qaUserId, isQaAccount: true));
            seed.Users.Add(MakeUser(realUserId, isQaAccount: false));
            AddPaidOrder(seed, MakeOrder(qaOrderId, qaUserId));
            AddPaidOrder(seed, MakeOrder(realOrderId, realUserId));

            seed.FuelVouchers.Add(MakeVoucher(realId, "real", isTestData: false));
            seed.FuelVouchers.Add(MakeVoucher(testForQaId, "test-for-qa", isTestData: true));
            seed.FuelVouchers.Add(MakeVoucher(testForRealId, "test-for-real", isTestData: true));

            await seed.SaveChangesAsync();
        }

        using var claimContext = CreateContext();
        var service = new TestableFulfillmentService(
            claimContext,
            new RefundOrderCommandHandler(claimContext, new StubMonobankClientFactory(new Mock<IMonobankClient>().Object), new AuditEventService(claimContext)),
            new RuntimeSettingsService(claimContext));

        // Real stock for the QA account: the claim goes through, exactly as it would for a customer.
        var qaFromReal = await service.ClaimAsync(realId, qaUserId, qaOrderId);
        qaFromReal.Should().Be(1);

        // Rehearsal stock for the QA account: refused, because the filter is on the row, not the buyer.
        var qaFromTest = await service.ClaimAsync(testForQaId, qaUserId, qaOrderId);
        qaFromTest.Should().Be(0);

        // Rehearsal stock for a real account: refused too.
        var realFromTest = await service.ClaimAsync(testForRealId, realUserId, realOrderId);
        realFromTest.Should().Be(0);

        using var verify = CreateContext();
        var claimed = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == realId);
        claimed.Status.Should().Be(VoucherStatus.Assigned);
        claimed.AssignedToUserId.Should().Be(qaUserId);
        (await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == testForQaId))
            .Status.Should().Be(VoucherStatus.Available);
        (await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == testForRealId))
            .Status.Should().Be(VoucherStatus.Available);
    }

    private static User MakeUser(Guid id, bool isQaAccount) => new()
    {
        Id = id,
        PhoneNumber = $"+38{id:N}"[..20],
        IsActive = true,
        IsQaAccount = isQaAccount,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private static Order MakeOrder(Guid orderId, Guid userId) => new()
    {
        Id = orderId,
        UserId = userId,
        Price = 520,
        Status = OrderStatus.PendingFulfillment,
        CreatedAtUtc = TestClock.UtcNow.AddDays(-1),
        UpdatedAtUtc = TestClock.UtcNow.AddDays(-1),
        LineItems = new List<OrderLineItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Brand = Brand,
                    BrandId = BrandIds.ForSlug(Brand),
                    FuelTypeId =   ToUuid(FuelTypeId),
                Liters = Liters,
                Quantity = 1,
                UnitPrice = 520,
                LineTotal = 520
            }
        }
    };

    private static FuelVoucher MakeVoucher(Guid id, string tag, bool isTestData) => new()
    {
        Id = id,
        Brand = Brand,
        BrandId = BrandIds.ForSlug(Brand),
        FuelTypeId =   ToUuid(FuelTypeId),
        Liters = Liters,
        SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(3),
        CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(3),
        VoucherNumber = $"{tag}-{id:N}",
        QrPayload = $"{tag}-qr-{id:N}",
        Status = VoucherStatus.Available,
        IsTestData = isTestData,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private FuelFlow.API.BackgroundJobs.FulfillmentService CreateService(ApplicationDbContext context)
        => new(
            context,
            NullLogger<FuelFlow.API.BackgroundJobs.FulfillmentService>.Instance,
            new RefundOrderCommandHandler(context, new StubMonobankClientFactory(new Mock<IMonobankClient>().Object), new AuditEventService(context)),
            new RuntimeSettingsService(context),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build(),
            LedgerTestDoubles.VatRelease(context));

    /// <summary>Exposes the protected atomic claim so the row-kind backstop can be tested directly,
    /// independent of the candidate SELECT that already filters by kind.</summary>
    private sealed class TestableFulfillmentService : FuelFlow.API.BackgroundJobs.FulfillmentService
    {
        public TestableFulfillmentService(
            ApplicationDbContext context,
            RefundOrderCommandHandler refundHandler,
            RuntimeSettingsService settings)
            : base(context, NullLogger<FuelFlow.API.BackgroundJobs.FulfillmentService>.Instance, refundHandler, settings, NotificationDispatcher.Disabled, new ConfigurationBuilder().Build(), LedgerTestDoubles.VatRelease(context))
        {
        }

        public Task<int> ClaimAsync(Guid voucherId, Guid userId, Guid orderId)
            => TryAssignVoucherAsync(voucherId, userId, null, orderId, null, CancellationToken.None);
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
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
    }
}
