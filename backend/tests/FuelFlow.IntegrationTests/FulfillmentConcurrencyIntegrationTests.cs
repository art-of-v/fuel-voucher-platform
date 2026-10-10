using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

[Collection("Integration Tests")]
public sealed class FulfillmentConcurrencyIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FulfillmentConcurrencyIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentFulfillmentRuns_AssignExactlyTheOrderedVouchers()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string provider = "OKKO";
        const string fuelTypeId = "okko-95";
        const decimal liters = 50m;
        const int quantity = 3;
        const int availableCount = 200;

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            // The fulfillment service scans ALL open orders, so leftover state from a previous
            // test in this shared class-fixture database would otherwise let an older order claim
            // this test's vouchers. Wipe the domain tables to keep each test hermetic.
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
                Price = quantity * 2500,
                Status = OrderStatus.PendingFulfillment,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        Provider = provider,
                        FuelTypeId = fuelTypeId,
                        Liters = liters,
                        Quantity = quantity,
                        UnitPrice = 2500,
                        LineTotal = quantity * 2500
                    }
                }
            });

            for (var i = 0; i < availableCount; i++)
            {
                seed.FuelVouchers.Add(new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    FuelTypeId = fuelTypeId,
                    Liters = liters,
                    ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    VoucherNumber = $"OKKO-{orderId:N}-{i:D3}",
                    QrPayload = $"payload-{orderId:N}-{i:D3}",
                    Status = VoucherStatus.Available,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            await seed.SaveChangesAsync();
        }

        var barrier = new DualBarrier();
        var monobankMock = new Mock<IMonobankClient>();
        monobankMock.Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        using (var serviceA = CreateService(barrier, monobankMock.Object))
        using (var serviceB = CreateService(barrier, monobankMock.Object))
        {
            await Task.WhenAll(
                serviceA.ProcessPendingOrdersAsync(),
                serviceB.ProcessPendingOrdersAsync());
        }

        using var verify = CreateContext();
        var fulfillments = await verify.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().HaveCount(quantity);

        var order = await verify.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        order.Should().NotBeNull();
        order!.Status.Should().Be(OrderStatus.Fulfilled);

        var assignedVouchers = await verify.FuelVouchers
            .AsNoTracking()
            .Where(v => v.AssignedToUserId == userId && v.Status == VoucherStatus.Assigned)
            .ToListAsync();
        assignedVouchers.Should().HaveCount(quantity);

        var assignedVoucherIds = assignedVouchers.Select(v => v.Id).ToHashSet();
        var fulfilledVoucherIds = fulfillments.Select(f => f.VoucherId).ToHashSet();
        assignedVoucherIds.Should().BeEquivalentTo(fulfilledVoucherIds);
    }

    [Fact]
    public async Task PartialFulfillment_WithTwoOfThreeVouchers_ShouldBecomePartiallyFulfilled()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string provider = "okko";
        const string fuelTypeId = "okko-dp";
        const decimal liters = 10m;
        const int quantity = 3;

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            // The fulfillment service scans ALL open orders, so leftover state from a previous
            // test in this shared class-fixture database would otherwise let an older order claim
            // this test's vouchers. Wipe the domain tables to keep each test hermetic.
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
                Price = quantity * 520,
                Status = OrderStatus.PendingFulfillment,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        Provider = provider,
                        FuelTypeId = fuelTypeId,
                        Liters = liters,
                        Quantity = quantity,
                        UnitPrice = 520,
                        LineTotal = quantity * 520
                    }
                }
            });

            for (var i = 0; i < 2; i++)
            {
                seed.FuelVouchers.Add(new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Provider = "OKKO",
                    FuelTypeId = fuelTypeId,
                    Liters = liters,
                    ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    VoucherNumber = $"PF-{orderId:N}-{i:D3}",
                    QrPayload = $"payload-{orderId:N}-{i:D3}",
                    Status = VoucherStatus.Available,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            await seed.SaveChangesAsync();
        }

        using (var service = CreateService(new DualBarrier(), new Mock<IMonobankClient>().Object))
        {
            await service.ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();
        var fulfillments = await verify.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().HaveCount(2);

        var order = await verify.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        order!.Status.Should().Be(OrderStatus.PartiallyFulfilled);

        var assignedVouchers = await verify.FuelVouchers
            .AsNoTracking()
            .Where(v => v.AssignedToUserId == userId && v.Status == VoucherStatus.Assigned)
            .ToListAsync();
        assignedVouchers.Should().HaveCount(2);
    }

    [Fact]
    public async Task FullRun_MultipleOrdersInOneContext_ShouldNotCrashWithDetached()
    {
        var userId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            // The fulfillment service scans ALL open orders, so leftover state from a previous
            // test in this shared class-fixture database would otherwise let an older order claim
            // this test's vouchers. Wipe the domain tables to keep each test hermetic.
            await ResetDataAsync(seed);

            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{userId:N}"[..20],
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            AddOrder(seed, userId, "okko-95", 50m, 2500, quantity: 1, voucherCount: 1, "FULL");
            AddOrder(seed, userId, "okko-dp", 10m, 520, quantity: 3, voucherCount: 2, "PARTIAL");

            await seed.SaveChangesAsync();
        }

        using (var service = CreateService(new DualBarrier(), new Mock<IMonobankClient>().Object))
        {
            await service.ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();
        var orders = await verify.Orders.ToListAsync();
        orders.Should().Contain(o => o.Status == OrderStatus.Fulfilled);
        orders.Should().Contain(o => o.Status == OrderStatus.PartiallyFulfilled);
    }

    private static void AddOrder(
        ApplicationDbContext seed,
        Guid userId,
        string fuelTypeId,
        decimal liters,
        int unitPrice,
        int quantity,
        int voucherCount,
        string prefix)
    {
        var orderId = Guid.NewGuid();

        seed.Orders.Add(new Order
        {
            Id = orderId,
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
                    OrderId = orderId,
                    Provider = "OKKO",
                    FuelTypeId = fuelTypeId,
                    Liters = liters,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    LineTotal = quantity * unitPrice
                }
            }
        });

        for (var i = 0; i < voucherCount; i++)
        {
            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "OKKO",
                FuelTypeId = fuelTypeId,
                Liters = liters,
                ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                VoucherNumber = $"{prefix}-{orderId:N}-{i:D3}",
                QrPayload = $"payload-{orderId:N}-{i:D3}",
                Status = VoucherStatus.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
    }

    [Fact]
    public async Task OrderWithExistingFulfillment_Backfill_ShouldAssignRemainingWithoutCrash()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            // The fulfillment service scans ALL open orders, so leftover state from a previous
            // test in this shared class-fixture database would otherwise let an older order claim
            // this test's vouchers. Wipe the domain tables to keep each test hermetic.
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
                Price = 3 * 520,
                Status = OrderStatus.PartiallyFulfilled,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        Provider = "OKKO",
                        FuelTypeId = "okko-dp",
                        Liters = 10m,
                        Quantity = 3,
                        UnitPrice = 520,
                        LineTotal = 3 * 520
                    }
                }
            });

            var usedVoucherId = Guid.NewGuid();
            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = usedVoucherId,
                Provider = "OKKO",
                FuelTypeId = "okko-dp",
                Liters = 10m,
                ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                VoucherNumber = $"EX-{orderId:N}-used",
                QrPayload = $"payload-{orderId:N}-used",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                OrderId = orderId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = usedVoucherId,
                FulfilledAtUtc = DateTime.UtcNow.AddHours(-2)
            });

            for (var i = 0; i < 2; i++)
            {
                seed.FuelVouchers.Add(new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Provider = "OKKO",
                    FuelTypeId = "okko-dp",
                    Liters = 10m,
                    ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    VoucherNumber = $"EX-{orderId:N}-avail-{i}",
                    QrPayload = $"payload-{orderId:N}-avail-{i}",
                    Status = VoucherStatus.Available,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            await seed.SaveChangesAsync();
        }

        using (var service = CreateService(new DualBarrier(), new Mock<IMonobankClient>().Object))
        {
            await service.ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();
        var fulfillments = await verify.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().HaveCount(3);

        var order = await verify.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        order!.Status.Should().Be(OrderStatus.Fulfilled);
    }

    [Fact]
    public async Task AutoRefundAfterPartialFulfillment_ShouldNotCorruptTrackerForNextOrder()
    {
        var userId = Guid.NewGuid();
        var partialOrderId = Guid.NewGuid();
        var fullOrderId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            // The fulfillment service scans ALL open orders, so leftover state from a previous
            // test in this shared class-fixture database would otherwise let an older order claim
            // this test's vouchers. Wipe the domain tables to keep each test hermetic.
            await ResetDataAsync(seed);

            seed.AppSettings.Add(new AppSetting
            {
                Key = AppSettingKeys.AutoRefundEnabled,
                Value = "true",
                UpdatedAtUtc = DateTime.UtcNow
            });
            seed.AppSettings.Add(new AppSetting
            {
                Key = AppSettingKeys.AutoRefundDelayDays,
                Value = "0",
                UpdatedAtUtc = DateTime.UtcNow
            });

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
                Id = partialOrderId,
                UserId = userId,
                Price = 3 * 520,
                Status = OrderStatus.PendingFulfillment,
                MonobankInvoiceId = $"test-invoice-{partialOrderId:N}",
                // Paid. Reaching PendingFulfillment means a `success` webhook applied the
                // transition and that handler records MonobankStatus=Success, so an order this
                // service is asked to auto-refund is one money was collected for. Stated
                // explicitly because refundable value is now gated on the provider's word.
                MonobankStatus = MonobankStatus.Success,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-2),
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = partialOrderId,
                        Provider = "OKKO",
                        FuelTypeId = "okko-dp",
                        Liters = 10m,
                        Quantity = 3,
                        UnitPrice = 520,
                        LineTotal = 3 * 520
                    }
                }
            });

            for (var i = 0; i < 2; i++)
            {
                seed.FuelVouchers.Add(new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Provider = "OKKO",
                    FuelTypeId = "okko-dp",
                    Liters = 10m,
                    ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    VoucherNumber = $"AR-{partialOrderId:N}-{i:D3}",
                    QrPayload = $"payload-{partialOrderId:N}-{i:D3}",
                    Status = VoucherStatus.Available,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            seed.Orders.Add(new Order
            {
                Id = fullOrderId,
                UserId = userId,
                Price = 2 * 2500,
                Status = OrderStatus.PendingFulfillment,
                MonobankInvoiceId = $"test-invoice-{fullOrderId:N}",
                MonobankStatus = MonobankStatus.Success,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
                LineItems = new List<OrderLineItem>
                {
                    new OrderLineItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = fullOrderId,
                        Provider = "OKKO",
                        FuelTypeId = "okko-95",
                        Liters = 50m,
                        Quantity = 2,
                        UnitPrice = 2500,
                        LineTotal = 2 * 2500
                    }
                }
            });

            for (var i = 0; i < 2; i++)
            {
                seed.FuelVouchers.Add(new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50m,
                    ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                    VoucherNumber = $"AR-{fullOrderId:N}-{i:D3}",
                    QrPayload = $"payload-{fullOrderId:N}-{i:D3}",
                    Status = VoucherStatus.Available,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            await seed.SaveChangesAsync();
        }

        var monobankMock = new Mock<IMonobankClient>();
        monobankMock.Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        using (var service = CreateService(new DualBarrier(), monobankMock.Object))
        {
            await service.ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();

        var partialOrder = await verify.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == partialOrderId);
        partialOrder.Should().NotBeNull();
        // The refund is in flight (Monobank returns "processing"), so the order keeps its
        // fulfillment-derived status until RefundStatusSyncService confirms the cancel.
        partialOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);

        var fullOrder = await verify.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == fullOrderId);
        fullOrder.Should().NotBeNull();
        fullOrder!.Status.Should().Be(OrderStatus.Fulfilled);

        var fullOrderFulfillments = await verify.Fulfillments
            .Where(f => f.OrderId == fullOrderId)
            .ToListAsync();
        fullOrderFulfillments.Should().HaveCount(2);

        var refunds = await verify.Refunds
            .Where(r => r.OrderId == partialOrderId)
            .ToListAsync();
        refunds.Should().ContainSingle(r => r.Status == RefundStatus.Processing);
    }

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

    private ConcurrentFulfillmentService CreateService(DualBarrier barrier, IMonobankClient monobankClient)
    {
        var context = CreateContext();
        var refundHandler = new RefundOrderCommandHandler(
            context,
            new StubMonobankClientFactory(monobankClient),
            new ProviderEventService(context));
        return new ConcurrentFulfillmentService(
            context,
            barrier,
            NullLogger<FuelFlow.API.BackgroundJobs.FulfillmentService>.Instance,
            refundHandler,
            new RuntimeSettingsService(context));
    }

    /// <summary>
    /// Hooks the first voucher-claim so two service instances are guaranteed to both be
    /// "in flight" (past the already-assigned count read) before either assigns. Without the
    /// per-order lock this deterministically over-assigns; with it the second instance blocks
    /// on the advisory lock and never reaches the hook.
    /// </summary>
    private sealed class ConcurrentFulfillmentService : FuelFlow.API.BackgroundJobs.FulfillmentService, IDisposable
    {
        private readonly DualBarrier _barrier;
        private readonly ApplicationDbContext _context;

        public ConcurrentFulfillmentService(
            ApplicationDbContext context,
            DualBarrier barrier,
            ILogger<FuelFlow.API.BackgroundJobs.FulfillmentService> logger,
            RefundOrderCommandHandler refundHandler,
            RuntimeSettingsService settings)
            : base(context, logger, refundHandler, settings, FuelFlow.SharedKernel.Observability.NotificationDispatcher.Disabled, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
        {
            _context = context;
            _barrier = barrier;
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        protected internal override async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, Guid? legalEntityId, Guid orderId, DateOnly? customerExpiration, CancellationToken cancellationToken)
        {
            _barrier.SignalArrival();
            await _barrier.WaitForPeerAsync();
            return await base.TryAssignVoucherAsync(voucherId, userId, legalEntityId, orderId, customerExpiration, cancellationToken);
        }
    }

    private sealed class DualBarrier
    {
        private readonly TaskCompletionSource _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SignalArrival()
        {
            _first.TrySetResult();
        }

        public Task WaitForPeerAsync() => _first.Task;
    }
}
