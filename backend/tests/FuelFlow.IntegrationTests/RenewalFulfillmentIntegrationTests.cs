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
using FuelFlow.Features.Vouchers.Exchange;
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
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, sourceExpiry));
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
        source.CustomerExpirationDate.Should().Be(sourceExpiry.AddMonths(1)); // OLD expiry + term, leftover days kept

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().Be(sourceId);
        item.FulfilledAtUtc.Should().NotBeNull();

        var fulfillments = await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == orderId).ToListAsync();
        fulfillments.Should().ContainSingle().Which.VoucherId.Should().Be(sourceId);
    }

    /// <summary>
    /// A still-valid voucher whose supplier term cannot absorb the bought term must be replaced from stock,
    /// not left hanging.
    ///
    /// Checkout prices this line as a Replace for exactly this reason - the source still has customer
    /// validity but no room under its supplier term - and the customer pays for a replacement. Fulfilment
    /// used to re-decide the branch on the date alone, so it chose Extend, hit the supplier ceiling, refused,
    /// and left the line unfulfilled: a paid order that silently stalled. The date cannot pick the branch
    /// here; the ceiling has to, same as at sale time.
    /// </summary>
    [Fact]
    public async Task SupplierTermExhausted_ReplacesFromStockInsteadOfStallingThePaidOrder()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(5);
        var providerExpiry = today.AddDays(5); // customer term == supplier term: no room to grow at all

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);

            // The source voucher was delivered by an earlier purchase, not by the renewal order
            // under test.
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, sourceExpiry, providerExpiry));
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 50m, today.AddMonths(6)));
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
        order.Status.Should().Be(OrderStatus.Fulfilled, "the customer paid for a replacement, so it must be given");

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().Be(stockId);

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.CustomerExpirationDate.Should().Be(sourceExpiry, "the retired voucher's dates are left alone");
        source.AssignedToUserId.Should().BeNull("the replaced voucher is owed to the supplier");

        var stock = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == stockId);
        stock.Status.Should().Be(VoucherStatus.Assigned);
        stock.AssignedToUserId.Should().Be(userId);
        stock.ProviderExpirationDate.Should().Be(today.AddMonths(6), "the supplier term stays as printed");
        stock.CustomerExpirationDate.Should().BeOnOrAfter(today.AddMonths(1),
            "the replacement must cover at least the term that was bought and paid for");
        stock.CustomerExpirationDate.Should().Be(stock.ProviderExpirationDate,
            "stock carries its full paper term: the replacement never shortens validity the station will honour");
    }

    /// <summary>
    /// The ceiling, against real Postgres, when there is NO stock to swap in. We cannot manufacture validity
    /// the supplier never granted, so nothing is applied and the line stays open for the backfill.
    ///
    /// Before this guard the raw UPDATE was unconditional on the expiry columns, so the row would have
    /// been stamped a month beyond what the supplier voucher actually covers (#162).
    /// </summary>
    [Fact]
    public async Task SupplierTermExhausted_WithoutStock_LeavesTheLineOpenRatherThanOverExtending()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(5);
        var providerExpiry = today.AddDays(6); // one day of real life left - a 1-month term cannot fit

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, sourceExpiry, providerExpiry));
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

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.CustomerExpirationDate.Should().Be(sourceExpiry,
            "the customer date must not move past the supplier's real term");
        source.ProviderExpirationDate.Should().Be(providerExpiry, "the supplier term is immutable");
        source.Status.Should().Be(VoucherStatus.Assigned);

        // The line stays unfulfilled so the per-minute backfill retries it once stock exists, and the
        // order is left open rather than being marked Fulfilled on a promise we did not keep.
        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().BeNull();
        item.FulfilledAtUtc.Should().BeNull();

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().NotBe(OrderStatus.Fulfilled);

        (await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == orderId).CountAsync())
            .Should().Be(0);
    }

    /// <summary>
    /// The happy path of the ceiling: a term that lands exactly on the supplier's real term is allowed,
    /// and the provider date itself is never touched by an extension.
    /// </summary>
    [Fact]
    public async Task ExtendBranch_ExtendingExactlyToTheSupplierTermIsAllowed()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(10);
        var providerExpiry = sourceExpiry.AddMonths(1); // exactly one month of room

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, sourceExpiry, providerExpiry));
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

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.CustomerExpirationDate.Should().Be(providerExpiry, "the extension lands exactly on the ceiling");
        source.ProviderExpirationDate.Should().Be(providerExpiry, "the supplier term never moves");
        source.Status.Should().Be(VoucherStatus.Assigned);

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.Fulfilled);
    }

    [Fact]
    public async Task ReplaceBranch_LapsedSource_AssignsStockAndExpiresOldVoucher()    {
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
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            // Source lapsed → Replace branch. Seeded Assigned so we can prove it flips to Expired.
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 40m, today.AddDays(-3)));
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
        source.AssignedToUserId.Should().BeNull(
            "the replaced voucher is owed to the supplier, so ownership must go back to the operator");
        source.LegalEntityId.Should().BeNull();
        source.WorkerUserId.Should().BeNull();

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().Be(stockId); // marker points at the fresh voucher

        var fulfillments = await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == orderId).ToListAsync();
        fulfillments.Should().ContainSingle().Which.VoucherId.Should().Be(stockId);
    }

    /// <summary>
    /// The whole point of releasing the replaced voucher: the chain a customer's extension must leave
    /// behind, from the voucher they gave up to what the supplier eventually charged us for it.
    ///
    /// Before this the replaced voucher kept its assignee and stayed invisible to
    /// <c>GetVoucherExchangeAttentionQueryHandler</c> (stock-only), and nothing recorded that it was owed
    /// to the supplier — so the surcharge that made it whole never landed anywhere auditable.
    /// </summary>
    [Fact]
    public async Task ReplacedVoucher_ShowsUpInExchangeAttention_AsOwedToTheSupplier()
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

            // The lapsed voucher was originally delivered by a purchase, not by the renewal order
            // under test.
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 40m, today.AddDays(-3)));
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
        var handler = new GetVoucherExchangeAttentionQueryHandler(
            verify, new RuntimeSettingsService(verify));

        var attention = await handler.HandleAsync(CancellationToken.None);

        // The voucher the customer gave up is now stock, so the operator sees it as work to do.
        var entry = attention.Data.Should().ContainSingle(i => i.Id == sourceId).Which;
        entry.Status.Should().Be(nameof(VoucherStatus.Expired));
        entry.ReleasedFromCustomer.Should().BeTrue(
            "it reached us through a customer replacement, not by ageing out of stock");

        // The voucher issued in its place belongs to the customer, so it must NOT appear here.
        attention.Data.Should().NotContain(i => i.Id == stockId);

        // The link that makes the cost chain walkable: this voucher's renewal line names the fresh one.
        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.SourceVoucherId.Should().Be(sourceId);
        item.FulfilledVoucherId.Should().Be(stockId);

        // And the customer it used to belong to stays reachable through the order it was sold on.
        (await verify.Fulfillments.AsNoTracking().AnyAsync(f => f.OrderId == orderId))
            .Should().BeTrue();
        (await verify.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.UserId).FirstAsync())
            .Should().Be(userId);
    }

    /// <summary>
    /// Stock that simply aged out must not be mistaken for a voucher we owe the supplier, or the operator
    /// cannot tell the two apart in the exchange list.
    /// </summary>
    [Fact]
    public async Task StockThatLapsed_IsNotFlaggedAsReleasedFromCustomer()
    {
        var lapsedStockId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.FuelVouchers.Add(StockVoucher(lapsedStockId, "OKKO", "okko-95", 40m, today.AddDays(-10)));

            await seed.SaveChangesAsync();
        }

        using var verify = CreateContext();
        var handler = new GetVoucherExchangeAttentionQueryHandler(
            verify, new RuntimeSettingsService(verify));

        var attention = await handler.HandleAsync(CancellationToken.None);

        var entry = attention.Data.Should().ContainSingle(i => i.Id == lapsedStockId).Which;
        entry.ReleasedFromCustomer.Should().BeFalse();
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

            // One purchase order originally delivered BOTH source vouchers (quantity 2) — it is not the
            // renewal order under test, which the service creates and owns on its own.
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            // Line A: still-valid source → Extend, will succeed.
            seed.FuelVouchers.Add(SourceVoucher(extendSourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, today.AddDays(5)));
            // Line B: lapsed source → Replace, but NO matching stock is seeded → stays unfulfilled.
            seed.FuelVouchers.Add(SourceVoucher(replaceSourceId, userId, purchaseOrderId, "WOG", "wog-95", 40m, today.AddDays(-3)));

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

    /// <summary>
    /// The purchase that originally delivered a source voucher. A held voucher (Assigned/Used/Blocked)
    /// must carry an <c>order_id</c> — ck_voucher_held_has_order rejects the row otherwise — and for a
    /// source voucher that order is the original purchase, never the renewal order under test (the
    /// service creates that one itself and writes its own order_id onto whatever it hands over).
    /// </summary>
    private static Guid SeedPurchaseOrder(ApplicationDbContext ctx, Guid userId)
    {
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 0,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        return orderId;
    }

    private static FuelVoucher SourceVoucher(Guid id, Guid userId, Guid orderId, string provider, string fuelTypeId, decimal liters, DateOnly expiry, DateOnly? providerExpiry = null)
        => new()
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ProviderExpirationDate = providerExpiry ?? expiry.AddYears(1),
            CustomerExpirationDate = expiry,
            VoucherNumber = $"SRC-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
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
            ProviderExpirationDate = expiry,
            CustomerExpirationDate = expiry,
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
