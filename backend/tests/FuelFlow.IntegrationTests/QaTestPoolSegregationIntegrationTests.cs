using FluentAssertions;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The QA⇄test segregation that makes <c>is_test_data</c> safe to run in production: a QA account
/// (<see cref="User.IsQaAccount"/>) is only ever handed emulated test stock, and every real account
/// is only ever handed real stock. Without it a seeded test voucher could be assigned to a paying
/// customer, or a QA run could quietly burn real inventory a customer had paid for.
///
/// Enforced in two places, both exercised here against real Postgres: the candidate SELECT in
/// fulfillment filters by the owner's pool, and the atomic claim UPDATE re-asserts it with a
/// correlated subquery so a flag edit between SELECT and claim can never cross the pools.
/// </summary>
[Collection("Integration Tests")]
public sealed class QaTestPoolSegregationIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const string Provider = "OKKO";
    private const string FuelTypeId = "okko-95";
    private const decimal Liters = 10m;

    private readonly TestDatabaseFixture _fixture;

    public QaTestPoolSegregationIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fulfillment_HandsAQaAccountTestStockAndARealAccountRealStock()
    {
        var qaUserId = Guid.NewGuid();
        var realUserId = Guid.NewGuid();
        var qaOrderId = Guid.NewGuid();
        var realOrderId = Guid.NewGuid();
        var testVoucherId = Guid.NewGuid();
        var realVoucherId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(MakeUser(qaUserId, isQaAccount: true));
            seed.Users.Add(MakeUser(realUserId, isQaAccount: false));

            seed.Orders.Add(MakeOrder(qaOrderId, qaUserId));
            seed.Orders.Add(MakeOrder(realOrderId, realUserId));

            // One voucher in each pool, identical on provider/fuel/nominal so only the flag can decide
            // who gets which.
            seed.FuelVouchers.Add(MakeVoucher(testVoucherId, "test", isTestData: true));
            seed.FuelVouchers.Add(MakeVoucher(realVoucherId, "real", isTestData: false));

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

        var testVoucher = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == testVoucherId);
        var realVoucher = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == realVoucherId);

        // The QA account got the test voucher; the real account got the real one. Neither crossed.
        testVoucher.AssignedToUserId.Should().Be(qaUserId);
        realVoucher.AssignedToUserId.Should().Be(realUserId);
    }

    [Fact]
    public async Task Fulfillment_WillNotDrainRealStockForAQaAccount()
    {
        var qaUserId = Guid.NewGuid();
        var qaOrderId = Guid.NewGuid();
        var realVoucherId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(MakeUser(qaUserId, isQaAccount: true));
            seed.Orders.Add(MakeOrder(qaOrderId, qaUserId));

            // Only real stock exists. A QA account must not be fulfilled from it.
            seed.FuelVouchers.Add(MakeVoucher(realVoucherId, "real", isTestData: false));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await CreateService(ctx).ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();

        var qaOrder = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == qaOrderId);
        qaOrder.Status.Should().Be(OrderStatus.PendingFulfillment);

        var realVoucher = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == realVoucherId);
        realVoucher.Status.Should().Be(VoucherStatus.Available);
        realVoucher.AssignedToUserId.Should().BeNull();

        (await verify.Fulfillments.AsNoTracking().CountAsync(f => f.OrderId == qaOrderId))
            .Should().Be(0);
    }

    [Fact]
    public async Task TheAtomicClaim_RefusesCrossPoolStockButAllowsTheOwnPool()
    {
        var qaUserId = Guid.NewGuid();
        var realUserId = Guid.NewGuid();
        var qaOrderId = Guid.NewGuid();
        var realOrderId = Guid.NewGuid();
        var realForQaId = Guid.NewGuid();
        var testForRealId = Guid.NewGuid();
        var testForQaId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(MakeUser(qaUserId, isQaAccount: true));
            seed.Users.Add(MakeUser(realUserId, isQaAccount: false));
            seed.Orders.Add(MakeOrder(qaOrderId, qaUserId));
            seed.Orders.Add(MakeOrder(realOrderId, realUserId));

            seed.FuelVouchers.Add(MakeVoucher(realForQaId, "real-for-qa", isTestData: false));
            seed.FuelVouchers.Add(MakeVoucher(testForRealId, "test-for-real", isTestData: true));
            seed.FuelVouchers.Add(MakeVoucher(testForQaId, "test-for-qa", isTestData: true));

            await seed.SaveChangesAsync();
        }

        using var claimContext = CreateContext();
        var service = new TestableFulfillmentService(
            claimContext,
            new RefundOrderCommandHandler(claimContext, new StubMonobankClientFactory(new Mock<IMonobankClient>().Object), new ProviderEventService(claimContext)),
            new RuntimeSettingsService(claimContext));

        // Real stock for a QA account: refused by the subquery, nothing written.
        var qaFromReal = await service.ClaimAsync(realForQaId, qaUserId, qaOrderId);
        qaFromReal.Should().Be(0);

        // Test stock for a real account: refused too.
        var realFromTest = await service.ClaimAsync(testForRealId, realUserId, realOrderId);
        realFromTest.Should().Be(0);

        // Test stock for the QA account: this is its own pool, so the claim goes through.
        var qaFromTest = await service.ClaimAsync(testForQaId, qaUserId, qaOrderId);
        qaFromTest.Should().Be(1);

        using var verify = CreateContext();
        (await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == realForQaId))
            .Status.Should().Be(VoucherStatus.Available);
        (await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == testForRealId))
            .Status.Should().Be(VoucherStatus.Available);

        var claimed = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == testForQaId);
        claimed.Status.Should().Be(VoucherStatus.Assigned);
        claimed.AssignedToUserId.Should().Be(qaUserId);
    }

    private static User MakeUser(Guid id, bool isQaAccount) => new()
    {
        Id = id,
        PhoneNumber = $"+38{id:N}"[..20],
        IsActive = true,
        IsQaAccount = isQaAccount,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Order MakeOrder(Guid orderId, Guid userId) => new()
    {
        Id = orderId,
        UserId = userId,
        Price = 520,
        Status = OrderStatus.PendingFulfillment,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
        LineItems = new List<OrderLineItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Provider = Provider,
                FuelTypeId = FuelTypeId,
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
        Provider = Provider,
        FuelTypeId = FuelTypeId,
        Liters = Liters,
        ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3),
        CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3),
        VoucherNumber = $"{tag}-{id:N}",
        QrPayload = $"{tag}-qr-{id:N}",
        Status = VoucherStatus.Available,
        IsTestData = isTestData,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);

    private FuelFlow.API.BackgroundJobs.FulfillmentService CreateService(ApplicationDbContext context)
        => new(
            context,
            NullLogger<FuelFlow.API.BackgroundJobs.FulfillmentService>.Instance,
            new RefundOrderCommandHandler(context, new StubMonobankClientFactory(new Mock<IMonobankClient>().Object), new ProviderEventService(context)),
            new RuntimeSettingsService(context),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build());

    /// <summary>Exposes the protected atomic claim so the subquery backstop can be tested directly,
    /// independent of the candidate SELECT that already filters by pool.</summary>
    private sealed class TestableFulfillmentService : FuelFlow.API.BackgroundJobs.FulfillmentService
    {
        public TestableFulfillmentService(
            ApplicationDbContext context,
            RefundOrderCommandHandler refundHandler,
            RuntimeSettingsService settings)
            : base(context, NullLogger<FuelFlow.API.BackgroundJobs.FulfillmentService>.Instance, refundHandler, settings, NotificationDispatcher.Disabled, new ConfigurationBuilder().Build())
        {
        }

        public Task<int> ClaimAsync(Guid voucherId, Guid userId, Guid orderId)
            => TryAssignVoucherAsync(voucherId, userId, null, orderId, null, CancellationToken.None);
    }
}
