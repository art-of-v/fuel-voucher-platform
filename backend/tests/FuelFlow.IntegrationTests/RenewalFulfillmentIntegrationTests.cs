using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Import;
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
        // History snapshot: the exact "valid from -> to" range this extend bought.
        item.PreviousCustomerExpiration.Should().Be(sourceExpiry);
        item.NewCustomerExpiration.Should().Be(sourceExpiry.AddMonths(1));

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
        source.Status.Should().Be(VoucherStatus.Available,
            "the replaced voucher goes back into the sellable pool — the customer walked away with a different one");
        source.AssignedToUserId.Should().BeNull("and nobody owns it any more");

        var stock = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == stockId);
        stock.Status.Should().Be(VoucherStatus.Assigned);
        stock.AssignedToUserId.Should().Be(userId);
        stock.ProviderExpirationDate.Should().Be(today.AddMonths(6), "the supplier term stays as printed");
        // The customer is promised what they paid for ON TOP of what they still held, and the stock was
        // chosen to reach at least that date — so this never needs clamping to the paper term.
        stock.CustomerExpirationDate.Should().Be(sourceExpiry.AddMonths(1),
            "the replacement carries exactly the promise: leftover term + the month that was paid for");

        // History snapshot: previous → paid, never the stock voucher's own (longer) term.
        item.PreviousCustomerExpiration.Should().Be(sourceExpiry);
        item.NewCustomerExpiration.Should().Be(sourceExpiry.AddMonths(1));
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
        var originPurchaseId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            originPurchaseId = purchaseOrderId;
            // Source lapsed → Replace branch. Seeded Assigned so we can prove it goes back to stock.
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
        source.Status.Should().Be(VoucherStatus.Available, // back in the sellable pool, not retired
            "the customer walked away with a different voucher, so this one is Fuel Flow's stock again");
        source.AssignedToUserId.Should().BeNull(
            "ownership goes back to the operator — the next claimer pays for it, so no double revenue");
        source.LegalEntityId.Should().BeNull();
        source.WorkerUserId.Should().BeNull();
        source.OrderId.Should().Be(originPurchaseId,
            "the link to the purchase that delivered this paper is kept — re-homing it would rewrite the origin");

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().Be(stockId); // marker points at the fresh voucher

        var fulfillments = await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == orderId).ToListAsync();
        fulfillments.Should().ContainSingle().Which.VoucherId.Should().Be(stockId);
    }

    /// <summary>
    /// The chain a customer's replacement leaves behind: the voucher they gave up is Fuel Flow's stock
    /// again, so the operator sees it as work to do, and it is still flagged as having reached us through a
    /// customer rather than by ageing out of the warehouse.
    ///
    /// Before the release the replaced voucher kept its assignee and stayed invisible to
    /// <c>GetVoucherExchangeAttentionQueryHandler</c> (stock-only), so nothing recorded the swap at all.
    /// </summary>
    [Fact]
    public async Task ReplacedVoucher_ReturnsToStock_AndIsFlaggedAsComingFromACustomer()
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
            // under test. Its PAPER has lapsed too, so once it is back in stock it is stock that is about
            // to be written off — which is exactly the case the operator's exchange list exists to surface.
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 40m,
                today.AddDays(-3), providerExpiry: today.AddDays(-3)));
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

        // The voucher the customer gave up is stock again, so the operator sees it as work to do.
        var entry = attention.Data.Should().ContainSingle(i => i.Id == sourceId).Which;
        entry.Status.Should().Be(nameof(VoucherStatus.Available));
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
    /// The owner's 9/14-days case, and the boundary that matters most: we may issue a voucher SHORTER than
    /// a longer one sitting in stock, but never one shorter than what was paid for. A customer paying for a
    /// week is owed until day 9, so the 9-day voucher goes out and the 14-day one is kept — and an 8-day
    /// voucher (the number the owner first used) would have been REJECTED, because it cannot carry the week.
    /// </summary>
    [Fact]
    public async Task Replace_PrefersTheShortestVoucherThatStillCoversAPaidWeek()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var nineDayId = Guid.NewGuid();
        var fourteenDayId = Guid.NewGuid();
        var eightDayId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(2);
        var promise = sourceExpiry.AddDays(7); // today + 9 days

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m,
                sourceExpiry, providerExpiry: sourceExpiry));
            seed.FuelVouchers.Add(StockVoucher(eightDayId, "OKKO", "okko-95", 50m, today.AddDays(8)));
            seed.FuelVouchers.Add(StockVoucher(nineDayId, "OKKO", "okko-95", 50m, today.AddDays(9)));
            seed.FuelVouchers.Add(StockVoucher(fourteenDayId, "OKKO", "okko-95", 50m, today.AddDays(14)));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = itemId,
                OrderId = orderId,
                SourceVoucherId = sourceId,
                TermCode = "1w",
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().Be(nineDayId,
            "the 9-day voucher reaches the promise exactly, so it beats the 14-day one on stock preservation");

        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == eightDayId)).Status
            .Should().Be(VoucherStatus.Available,
                "8 days cannot carry the week the customer paid for — that is the floor, not a preference");
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == fourteenDayId)).Status
            .Should().Be(VoucherStatus.Available, "it qualifies too, but the shorter voucher serves");

        var issued = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == nineDayId);
        issued.Status.Should().Be(VoucherStatus.Assigned);
        issued.CustomerExpirationDate.Should().Be(promise);
        item.NewCustomerExpiration.Should().Be(promise);
    }

    /// <summary>
    /// Two extends in a row on one voucher — the owner's own ladder (bought 1 week, +1 week, +2 months).
    /// Every step must add the term to what the customer ALREADY holds, on the same voucher with the same
    /// code, and must not touch the paper term. The third step in the owner's story runs past the ceiling and
    /// replaces, which <see cref="Replace_PicksTheShortestStockThatStillCoversThePromise"/> covers.
    /// </summary>
    [Fact]
    public async Task ExtendBranch_RepeatedRenewals_KeepAddingOnTopOfWhatTheCustomerHolds()
    {
        var userId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var boughtFor = today.AddDays(7);      // bought with a 1-week term
        var providerTerm = today.AddMonths(3); // the paper is good for 3 months

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m,
                boughtFor, providerExpiry: providerTerm));
            seed.Orders.Add(RenewalOrder(firstOrderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            seed.Orders.Add(RenewalOrder(secondOrderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = firstItemId,
                OrderId = firstOrderId,
                SourceVoucherId = sourceId,
                TermCode = "1w",
                CreatedAtUtc = DateTime.UtcNow
            });
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = secondItemId,
                OrderId = secondOrderId,
                SourceVoucherId = sourceId,
                TermCode = "2m",
                CreatedAtUtc = DateTime.UtcNow.AddSeconds(1)
            });

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(firstOrderId);
        }

        using (var afterFirst = CreateContext())
        {
            var v = await afterFirst.FuelVouchers.AsNoTracking().FirstAsync(x => x.Id == sourceId);
            v.Status.Should().Be(VoucherStatus.Assigned, "extend keeps the same voucher");
            v.CustomerExpirationDate.Should().Be(boughtFor.AddDays(7), "1 week on top of the week already held");
            v.ProviderExpirationDate.Should().Be(providerTerm, "the paper term is never touched");
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(secondOrderId);
        }

        using var verify = CreateContext();

        var final = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        final.Status.Should().Be(VoucherStatus.Assigned);
        final.AssignedToUserId.Should().Be(userId);
        final.QrPayload.Should().Be($"qr-{sourceId:N}", "still the same code — no new voucher, no new QR");
        final.ProviderExpirationDate.Should().Be(providerTerm);
        final.CustomerExpirationDate.Should().Be(boughtFor.AddDays(7).AddMonths(2),
            "2 months on top of the previous step, not on top of today");

        var firstItem = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == firstItemId);
        firstItem.FulfilledVoucherId.Should().Be(sourceId, "Extend points the item at the source voucher");
        firstItem.PreviousCustomerExpiration.Should().Be(boughtFor);
        firstItem.NewCustomerExpiration.Should().Be(boughtFor.AddDays(7));

        var secondItem = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == secondItemId);
        secondItem.FulfilledVoucherId.Should().Be(sourceId);
        secondItem.PreviousCustomerExpiration.Should().Be(boughtFor.AddDays(7));
        secondItem.NewCustomerExpiration.Should().Be(boughtFor.AddDays(7).AddMonths(2));

        // Two extensions must consume no stock at all.
        (await verify.FuelVouchers.AsNoTracking().AnyAsync(v => v.Status == VoucherStatus.Assigned && v.Id != sourceId))
            .Should().BeFalse("extend never hands over a different voucher");
    }

    /// <summary>
    /// The rule that decides WHICH stock voucher goes out: not "today + term", but the promise — the
    /// customer's leftover term plus what they just paid for — and among everything that reaches that
    /// date, the SHORTEST one.
    ///
    /// The concrete case the owner described: on 13.03 a customer holds fuel until 27.03 and pays for
    /// 2 months, so they are owed until 27.05. A 2-month voucher (good to 13.05) misses by two weeks and
    /// must be rejected; the 3-month one (13.06) qualifies, and handing that over keeps Fuel Flow's
    /// near-expiry stock from lapsing unused. The customer still only gets until 27.05 — the extra real
    /// life stays a reserve they can spend on a later renewal.
    /// </summary>
    [Fact]
    public async Task Replace_PicksTheShortestStockThatStillCoversThePromise()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var twoMonthsId = Guid.NewGuid();
        var threeMonthsId = Guid.NewGuid();
        var sixMonthsId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(14); // 27.03 in the owner's example
        var promise = sourceExpiry.AddMonths(2);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            // Customer term == provider term: no room to grow, so the branch is Replace.
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m,
                sourceExpiry, providerExpiry: sourceExpiry));
            seed.FuelVouchers.Add(StockVoucher(twoMonthsId, "OKKO", "okko-95", 50m, today.AddMonths(2)));
            seed.FuelVouchers.Add(StockVoucher(threeMonthsId, "OKKO", "okko-95", 50m, today.AddMonths(3)));
            seed.FuelVouchers.Add(StockVoucher(sixMonthsId, "OKKO", "okko-95", 50m, today.AddMonths(6)));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = itemId,
                OrderId = orderId,
                SourceVoucherId = sourceId,
                TermCode = "2m",
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        promise.Should().BeAfter(today.AddMonths(2), "the promise is measured from the customer's own date, so the 2-month voucher is short");
        promise.Should().BeBefore(today.AddMonths(3), "which is exactly why the 3-month voucher is the shortest that qualifies");

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().Be(threeMonthsId, "shortest qualifying voucher, not the longest");

        var issued = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == threeMonthsId);
        issued.Status.Should().Be(VoucherStatus.Assigned);
        issued.CustomerExpirationDate.Should().Be(promise,
            "the customer is given what they paid for on top of what they still held — the rest is reserve");
        issued.ProviderExpirationDate.Should().Be(today.AddMonths(3), "the paper term itself is untouched");

        item.PreviousCustomerExpiration.Should().Be(sourceExpiry);
        item.NewCustomerExpiration.Should().Be(promise);

        // The rejected candidates must still be sitting in stock, untouched.
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == twoMonthsId)).Status
            .Should().Be(VoucherStatus.Available, "too short to cover the promise, so not issued");
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sixMonthsId)).Status
            .Should().Be(VoucherStatus.Available, "longer than needed, so preserved for a tier that needs it");
    }

    /// <summary>
    /// When nothing reaches the promise we must NOT ship a short voucher and must NOT quietly clamp the
    /// promised date. The line simply stays open — the same posture the no-stock case already had, now
    /// reachable without an empty warehouse.
    /// </summary>
    [Fact]
    public async Task Replace_WithNoStockReachingThePromise_LeavesTheLineOpenRatherThanShippingAShortVoucher()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(10);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m,
                sourceExpiry, providerExpiry: sourceExpiry));
            // Both shorter than the promise (sourceExpiry + 2 months); the longer one still fits today + term.
            seed.FuelVouchers.Add(StockVoucher(Guid.NewGuid(), "OKKO", "okko-95", 50m, today.AddMonths(1)));
            seed.FuelVouchers.Add(StockVoucher(Guid.NewGuid(), "OKKO", "okko-95", 50m, today.AddMonths(2)));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = itemId,
                OrderId = orderId,
                SourceVoucherId = sourceId,
                TermCode = "2m",
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
        order.Status.Should().Be(OrderStatus.PendingFulfillment,
            "nothing was delivered, so the paid line must not be marked fulfilled");

        var item = await verify.VoucherRenewalItems.AsNoTracking().FirstAsync(i => i.Id == itemId);
        item.FulfilledVoucherId.Should().BeNull();
        item.NewCustomerExpiration.Should().BeNull("and no clamped date was written into the history");

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.Status.Should().Be(VoucherStatus.Assigned, "the customer keeps what they had");
        source.AssignedToUserId.Should().Be(userId);
        source.CustomerExpirationDate.Should().Be(sourceExpiry);

        (await verify.Fulfillments.AsNoTracking().AnyAsync(f => f.OrderId == orderId)).Should().BeFalse();
    }

    /// <summary>
    /// The voucher a customer gave back is stock again, so the NEXT customer to need fuel at the same
    /// price point gets that same voucher — and pays for it. That is what makes the rule harmless: the
    /// returned paper is recycled through the till rather than written off.
    /// </summary>
    [Fact]
    public async Task ReturnedVoucher_IsSoldAgainToTheNextCustomer_WhoPaysForIt()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(10);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, firstUserId);
            var purchaseOrderId = SeedPurchaseOrder(seed, firstUserId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, firstUserId, purchaseOrderId, "OKKO", "okko-95", 50m,
                sourceExpiry, providerExpiry: sourceExpiry));
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 50m, today.AddMonths(6)));
            seed.Orders.Add(RenewalOrder(firstOrderId, firstUserId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = itemId,
                OrderId = firstOrderId,
                SourceVoucherId = sourceId,
                TermCode = "2m",
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(firstOrderId);
        }

        using (var afterFirst = CreateContext())
        {
            var returned = await afterFirst.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
            returned.Status.Should().Be(VoucherStatus.Available);
            returned.AssignedToUserId.Should().BeNull();
        }

        // A different customer now buys fuel at the same price point. The buy matcher takes the shortest
        // eligible stock, and the returned voucher is now the shortest thing we own.
        using (var seed = CreateContext())
        {
            SeedUser(seed, secondUserId);
            seed.Orders.Add(new Order
            {
                Id = secondOrderId,
                UserId = secondUserId,
                Price = 500,
                Status = OrderStatus.PendingFulfillment,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                LineItems = new List<OrderLineItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        OrderId = secondOrderId,
                        Provider = "OKKO",
                        FuelTypeId = "okko-95",
                        Liters = 50m,
                        Quantity = 1,
                        UnitPrice = 500,
                        LineTotal = 500
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessPendingOrdersAsync();
        }

        using var verify = CreateContext();

        var resold = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        resold.Status.Should().Be(VoucherStatus.Assigned);
        resold.AssignedToUserId.Should().Be(secondUserId, "the next customer gets the recycled voucher");
        resold.OrderId.Should().Be(secondOrderId);

        (await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == secondOrderId)).Status
            .Should().Be(OrderStatus.Fulfilled, "and that customer paid for it, so there is no free voucher");

        (await verify.Fulfillments.AsNoTracking().FirstAsync(f => f.OrderId == firstOrderId)).VoucherId
            .Should().Be(stockId, "the renewal fulfilled the replacement; the recycled voucher is nobody's");
    }

    /// <summary>
    /// Two wallet guarantees at once, and they pull against each other.
    ///
    /// The released voucher must NOT be shown — it is Fuel Flow's stock again and the client has no status
    /// reason to hide an Available voucher. But it must still be LOADED, because resolving a replacement's
    /// origin walks back to the root voucher and reads ITS order_id: filter it out of the lookup and the
    /// replacement resolves to the RENEWAL order, which the wallet does not render — the customer's fuel
    /// then vanishes from their wallet entirely. So: hidden from the response, present for the chain.
    /// </summary>
    [Fact]
    public async Task Wallet_HidesTheReturnedVoucher_ButStillFilesTheReplacementUnderTheOriginalPurchase()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(10);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m,
                sourceExpiry, providerExpiry: sourceExpiry));
            // The purchase really did deliver this voucher, so the wallet can reach it. That row is what
            // puts the source into the customer's voucher chain — without it the origin walk has no root to
            // find and the replacement resolves to the renewal order, i.e. nowhere the wallet renders.
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = purchaseOrderId,
                VoucherId = sourceId,
                FulfilledAtUtc = DateTime.UtcNow.AddDays(-10)
            });
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 50m, today.AddMonths(6)));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            seed.VoucherRenewalItems.Add(new VoucherRenewalItem
            {
                Id = itemId,
                OrderId = orderId,
                SourceVoucherId = sourceId,
                TermCode = "2m",
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using (var verify = CreateContext())
        {
            var handler = new GetUserPurchasesCommandHandler(
                verify,
                new Mock<IQrGenerator>().Object,
                new NullLogger<GetUserPurchasesCommandHandler>());

            var purchases = await handler.HandleAsync(
                new GetUserPurchasesCommand(userId), CancellationToken.None);

            var shown = purchases.SelectMany(p => p.Vouchers).ToList();
            shown.Should().ContainSingle("the replacement is the only thing the customer still holds");
            shown.Should().NotContain(v => v.Id == sourceId,
                "the voucher they gave back is Fuel Flow's stock, not theirs");

            // The one that actually broke: the replacement must be filed under the ORIGINAL purchase, or
            // the wallet shows an empty list and the customer's fuel appears to have vanished.
            shown[0].OriginOrderId.Should().Be(
                (await verify.Orders.AsNoTracking().Where(o => o.UserId == userId && o.Kind == OrderKind.Purchase)
                    .Select(o => (Guid?)o.Id).FirstAsync()),
                "filed under the purchase the customer first made, not the renewal that delivered it");
        }
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

    /// <summary>
    /// Once the operator confirms the supplier exchange, the retired voucher has a voucher_exchanges row,
    /// so the renewal is done. It must then drop off BOTH the attention list and the badge count — not
    /// linger as a dead-end row whose only effect is that re-confirming it is rejected as "already
    /// exchanged", leaving the badge stuck (planning #183).
    /// </summary>
    [Fact]
    public async Task ExchangedVoucher_DropsOffTheAttentionListAndBadge()
    {
var exchangedId = Guid.NewGuid();
        var stillOwedId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Suppliers.Add(new Supplier
            {
                Id = supplierId,
                Name = "Seed Supplier",
                StationId = "okko",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            // Two lapsed stock vouchers the operator would normally have to renew with the supplier.
            seed.FuelVouchers.Add(StockVoucher(exchangedId, "OKKO", "okko-95", 40m, today.AddDays(-10)));
            seed.FuelVouchers.Add(StockVoucher(stillOwedId, "OKKO", "okko-95", 40m, today.AddDays(-10)));

            // One of them has already been exchanged with the supplier - the confirm handler wrote this row.
            seed.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = Guid.NewGuid(),
                OldVoucherId = exchangedId,
                FuelTypeId = "okko-95",
                Provider = "OKKO",
                SupplierId = supplierId,
                SurchargeUah = 0m,
                ActingUserId = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using var verify = CreateContext();
        var handler = new GetVoucherExchangeAttentionQueryHandler(
            verify, new RuntimeSettingsService(verify));

        var attention = await handler.HandleAsync(CancellationToken.None);

        // Only the voucher still owed to the supplier remains; the exchanged one is gone.
        attention.Data.Should().ContainSingle().Which.Id.Should().Be(stillOwedId);
        attention.Data.Should().NotContain(i => i.Id == exchangedId,
            "a voucher with a voucher_exchanges row has been renewed with the supplier — the job is done");

        // The nav badge must agree with the list, or it stays lit over nothing.
        (await handler.CountAsync(CancellationToken.None)).Should().Be(1);
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
            """TRUNCATE TABLE "refunds", "fulfillments", "voucher_renewal_items", "voucher_exchanges", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
