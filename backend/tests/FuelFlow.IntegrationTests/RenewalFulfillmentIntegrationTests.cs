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
using FuelFlow.Features.Vouchers.Renewal;
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
/// Testcontainers coverage for the renewal fulfilment path (Slice 3). Unlike the checkout handler
/// (LINQ-only, unit-tested on InMemory), <see cref="FulfillmentService.ProcessRenewalOrderAsync"/>
/// leans on raw-SQL atomic UPDATEs and a per-order advisory lock, so it only exercises faithfully
/// against real Postgres. Each test drives ONE paid renewal order through the two-branch logic:
/// extend-in-place, replace-from-stock, and the post-payment no-stock partial → auto-refund.
/// </summary>
[Collection("Integration Tests")]
public sealed class RenewalFulfillmentIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RenewalFulfillmentIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExtendBranch_StillValidSource_PushesExpiryAndMarksOrderFulfilled()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(5); // still valid → Extend

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, "OKKO", "okko-95", 50m, sourceExpiry));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = itemId,
                OrderId = orderId,
                SourceVoucherId = sourceId,
                TermCode = "1m",
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.Fulfilled);

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.Status.Should().Be(VoucherStatus.Assigned); // same row, still the customer's
        source.AssignedToUserId.Should().Be(userId);
        source.ExpirationDate.Should().Be(sourceExpiry.AddMonths(1)); // OLD expiry + term, leftover days kept

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().Be(sourceId);
        item.FulfilledAtUtc.Should().NotBeNull();

        var fulfillments = await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == orderId).ToListAsync();
        fulfillments.Should().ContainSingle().Which.VoucherId.Should().Be(sourceId);
    }

    [Fact]
    public async Task ReplaceBranch_LapsedSource_AssignsStockAndExpiresOldVoucher()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            // Source lapsed → Replace branch. Seeded Assigned so we can prove it flips to Expired.
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, "OKKO", "okko-95", 40m, today.AddDays(-3)));
            // Matching stock, valid well beyond today + 1 month.
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 40m, today.AddMonths(6)));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 480, monobankInvoiceId: null,
                ("OKKO", "okko-95", 40m, 480)));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = itemId,
                OrderId = orderId,
                SourceVoucherId = sourceId,
                TermCode = "1m",
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.Fulfilled);

        var stock = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == stockId);
        stock.Status.Should().Be(VoucherStatus.Assigned);
        stock.AssignedToUserId.Should().Be(userId);

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.Status.Should().Be(VoucherStatus.Expired); // old voucher retired

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().Be(stockId); // marker points at the fresh voucher

        var fulfillments = await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == orderId).ToListAsync();
        fulfillments.Should().ContainSingle().Which.VoucherId.Should().Be(stockId);
    }

    [Fact]
    public async Task NoStockReplaceLine_PartiallyFulfilledOrder_TriggersAutoRefund()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var extendSourceId = Guid.NewGuid();
        var replaceSourceId = Guid.NewGuid();
        var extendItemId = Guid.NewGuid();
        var replaceItemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            // Auto-refund on with a zero-day grace so the partial fires the refund immediately.
            seed.AppSettings.Add(new AppSetting { Key = AppSettingKeys.AutoRefundEnabled, Value = "true", UpdatedAtUtc = DateTime.UtcNow });
            seed.AppSettings.Add(new AppSetting { Key = AppSettingKeys.AutoRefundDelayDays, Value = "0", UpdatedAtUtc = DateTime.UtcNow });

            SeedUser(seed, userId);

            // Line A: still-valid source → Extend, will succeed.
            seed.FuelVouchers.Add(SourceVoucher(extendSourceId, userId, "OKKO", "okko-95", 50m, today.AddDays(5)));
            // Line B: lapsed source → Replace, but NO matching stock is seeded → stays unfulfilled.
            seed.FuelVouchers.Add(SourceVoucher(replaceSourceId, userId, "WOG", "wog-95", 40m, today.AddDays(-3)));

            seed.Orders.Add(RenewalOrder(orderId, userId, price: 980, monobankInvoiceId: $"test-invoice-{orderId:N}",
                ("OKKO", "okko-95", 50m, 500),
                ("WOG", "wog-95", 40m, 480)));

            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = extendItemId,
                OrderId = orderId,
                SourceVoucherId = extendSourceId,
                TermCode = "1m",
                CreatedAtUtc = DateTime.UtcNow
            });
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = replaceItemId,
                OrderId = orderId,
                SourceVoucherId = replaceSourceId,
                TermCode = "1m",
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        var monobankMock = new Mock<IMonobankClient>();
        monobankMock
            .Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, monobankMock.Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        // The refund is in flight (Monobank returns "processing"), so the order keeps its
        // fulfillment-derived status until RefundStatusSyncService confirms the cancel.
        order.Status.Should().Be(OrderStatus.PartiallyFulfilled);

        var extendItem = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == extendItemId);
        extendItem.FulfilledVoucherId.Should().Be(extendSourceId); // the extend line applied

        var replaceItem = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == replaceItemId);
        replaceItem.FulfilledVoucherId.Should().BeNull(); // no stock → left for the backfill / refund

        var refunds = await verify.Refunds.AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        refunds.Should().ContainSingle(r => r.Status == RefundStatus.Processing);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static FulfillmentService BuildService(ApplicationDbContext ctx, IMonobankClient monobankClient)
        => new(
            ctx,
            NullLogger<FulfillmentService>.Instance,
            new RefundOrderCommandHandler(ctx, monobankClient, new ProviderEventService(ctx)),
            new RuntimeSettingsService(ctx),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build());

    private static void SeedUser(ApplicationDbContext ctx, Guid userId)
        => ctx.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

    private static FuelVoucher SourceVoucher(Guid id, Guid userId, string provider, string fuelTypeId, decimal liters, DateOnly expiry)
        => new()
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ExpirationDate = expiry,
            VoucherNumber = $"SRC-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static FuelVoucher StockVoucher(Guid id, string provider, string fuelTypeId, decimal liters, DateOnly expiry)
        => new()
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ExpirationDate = expiry,
            VoucherNumber = $"STK-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static Order RenewalOrder(
        Guid orderId,
        Guid userId,
        int price,
        string? monobankInvoiceId,
        params (string Provider, string FuelTypeId, decimal Liters, int Amount)[] lines)
    {
        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = price,
            Status = OrderStatus.PendingFulfillment,
            MonobankInvoiceId = monobankInvoiceId,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            LineItems = new List<OrderLineItem>()
        };

        foreach (var (provider, fuelTypeId, liters, amount) in lines)
        {
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Provider = provider,
                FuelTypeId = fuelTypeId,
                Liters = liters,
                Quantity = 1,
                UnitPrice = amount,
                LineTotal = amount
            });
        }

        return order;
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "voucher_renewal_items", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
