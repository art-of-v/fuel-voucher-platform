using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.Renewal;
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

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Testcontainers coverage for the renewal fulfilment path (Slice 3). Unlike the checkout handler
/// (LINQ-only, unit-tested on InMemory), <see cref="FulfillmentService.ProcessRenewalOrderAsync"/>
/// leans on raw-SQL atomic UPDATEs and a per-order advisory lock, so it only exercises faithfully
/// against real Postgres. Each test drives ONE paid renewal order through the two-branch logic:
/// extend-in-place, replace-from-stock, and the post-payment no-stock partial ΓåÆ auto-refund.
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
        var sourceExpiry = today.AddDays(5); // still valid ΓåÆ Extend

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, sourceExpiry));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");

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

        var (extendLine, extendDelivered) = await LoadRenewalAsync(verify, orderId, sourceId);
        extendDelivered.Should().NotBeNull("an extend delivers the source voucher itself");
        extendDelivered!.FulfilledAtUtc.Should().BeOnOrBefore(DateTime.UtcNow, "a delivered line says when it was delivered");
        // History snapshot: the exact "valid from -> to" range this extend bought.
        extendLine.PreviousCustomerExpiration.Should().Be(sourceExpiry);
        extendLine.NewCustomerExpiration.Should().Be(sourceExpiry.AddMonths(1));

        // And on the order line, which is where the renewal's record is moving to (D-13). Asserted on
        // both sides because this is an expand step: the two are written from the same values in the
        // same loop iteration, and the contract step drops the old one.
        var lines = await verify.OrderLineItems.AsNoTracking().Where(l => l.OrderId == orderId).ToListAsync();
        var line = lines.Should().ContainSingle(because: "a renewal order has one renewal line").Subject;
        line.SourceVoucherId.Should().Be(sourceId);
        line.RenewalBranch.Should().Be(VoucherRenewalBranchCodes.Extend);
        line.PreviousCustomerExpiration.Should().Be(sourceExpiry);
        line.NewCustomerExpiration.Should().Be(sourceExpiry.AddMonths(1));

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
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.Fulfilled, "the customer paid for a replacement, so it must be given");

        var (replaceLine, replaceDelivered) = await LoadRenewalAsync(verify, orderId, sourceId);
        replaceDelivered!.VoucherId.Should().Be(stockId, "a replace delivers fresh stock, not the source");

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.CustomerExpirationDate.Should().Be(sourceExpiry, "the retired voucher's dates are left alone");
        source.Status.Should().Be(VoucherStatus.Available,
            "the replaced voucher goes back into the sellable pool ΓÇö the customer walked away with a different one");
        source.AssignedToUserId.Should().BeNull("and nobody owns it any more");

        var stock = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == stockId);
        stock.Status.Should().Be(VoucherStatus.Assigned);
        stock.AssignedToUserId.Should().Be(userId);
        stock.SupplierExpirationDate.Should().Be(today.AddMonths(6), "the supplier term stays as printed");
        // The customer is promised what they paid for ON TOP of what they still held, and the stock was
        // chosen to reach at least that date ΓÇö so this never needs clamping to the paper term.
        stock.CustomerExpirationDate.Should().Be(sourceExpiry.AddMonths(1),
            "the replacement carries exactly the promise: leftover term + the month that was paid for");

        // History snapshot: previous ΓåÆ paid, never the stock voucher's own (longer) term.
        replaceLine.PreviousCustomerExpiration.Should().Be(sourceExpiry);
        replaceLine.NewCustomerExpiration.Should().Be(sourceExpiry.AddMonths(1));
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
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");

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
        source.SupplierExpirationDate.Should().Be(providerExpiry, "the supplier term is immutable");
        source.Status.Should().Be(VoucherStatus.Assigned);

        // The line stays unfulfilled so the per-minute backfill retries it once stock exists, and the
        // order is left open rather than being marked Fulfilled on a promise we did not keep.
        var (_, noDelivery) = await LoadRenewalAsync(verify, orderId, sourceId);
        noDelivery.Should().BeNull("no stock meant nothing was delivered, so there is no fulfilment");

        var order = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        order.Status.Should().NotBe(OrderStatus.Fulfilled);

        (await verify.Fulfillments.AsNoTracking().Where(f => f.OrderId == orderId).CountAsync())
            .Should().Be(0);
    }

    /// <summary>
    /// The happy path of the ceiling: a term that lands exactly on the supplier's real term is allowed,
    /// and the brand date itself is never touched by an extension.
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
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.CustomerExpirationDate.Should().Be(providerExpiry, "the extension lands exactly on the ceiling");
        source.SupplierExpirationDate.Should().Be(providerExpiry, "the supplier term never moves");
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
            // Source lapsed ΓåÆ Replace branch. Seeded Assigned so we can prove it goes back to stock.
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 40m, today.AddDays(-3)));
            // Matching stock, valid well beyond today + 1 month.
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 40m, today.AddMonths(6)));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 480, monobankInvoiceId: null,
                ("OKKO", "okko-95", 40m, 480)));
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");

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
            "ownership goes back to the operator ΓÇö the next claimer pays for it, so no double revenue");
        source.LegalEntityId.Should().BeNull();
        source.WorkerUserId.Should().BeNull();
        source.OrderId.Should().Be(originPurchaseId,
            "the link to the purchase that delivered this paper is kept ΓÇö re-homing it would rewrite the origin");

        var (_, replacementDelivery) = await LoadRenewalAsync(verify, orderId, sourceId);
        replacementDelivery!.VoucherId.Should().Be(stockId); // points at the fresh voucher

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
            // to be written off ΓÇö which is exactly the case the operator's exchange list exists to surface.
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 40m,
                today.AddDays(-3), providerExpiry: today.AddDays(-3)));
            seed.FuelVouchers.Add(StockVoucher(stockId, "OKKO", "okko-95", 40m, today.AddMonths(6)));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 480, monobankInvoiceId: null,
                ("OKKO", "okko-95", 40m, 480)));
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");

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
        var (costChainLine, costChainDelivery) = await LoadRenewalAsync(verify, orderId, sourceId);
        costChainLine.SourceVoucherId.Should().Be(sourceId);
        costChainDelivery!.VoucherId.Should().Be(stockId);

        // And the customer it used to belong to stays reachable through the order it was sold on.
        (await verify.Fulfillments.AsNoTracking().AnyAsync(f => f.OrderId == orderId))
            .Should().BeTrue();
        (await verify.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.UserId).FirstAsync())
            .Should().Be(userId);
    }

    /// <summary>
    /// The owner's 9/14-days case, and the boundary that matters most: we may issue a voucher SHORTER than
    /// a longer one sitting in stock, but never one shorter than what was paid for. A customer paying for a
    /// week is owed until day 9, so the 9-day voucher goes out and the 14-day one is kept ΓÇö and an 8-day
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
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1w");

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        var (promisedLine, shortestDelivery) = await LoadRenewalAsync(verify, orderId, sourceId);
        shortestDelivery!.VoucherId.Should().Be(nineDayId,
            "the 9-day voucher reaches the promise exactly, so it beats the 14-day one on stock preservation");

        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == eightDayId)).Status
            .Should().Be(VoucherStatus.Available,
                "8 days cannot carry the week the customer paid for ΓÇö that is the floor, not a preference");
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == fourteenDayId)).Status
            .Should().Be(VoucherStatus.Available, "it qualifies too, but the shorter voucher serves");

        var issued = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == nineDayId);
        issued.Status.Should().Be(VoucherStatus.Assigned);
        issued.CustomerExpirationDate.Should().Be(promise);
        promisedLine.NewCustomerExpiration.Should().Be(promise);
    }

    /// <summary>
    /// Two extends in a row on one voucher ΓÇö the owner's own ladder (bought 1 week, +1 week, +2 months).
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
            MarkAsRenewalLine(seed, firstOrderId, sourceId, termCode: "1w");
            MarkAsRenewalLine(seed, secondOrderId, sourceId, termCode: "2m");

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
            v.SupplierExpirationDate.Should().Be(providerTerm, "the paper term is never touched");
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(secondOrderId);
        }

        using var verify = CreateContext();

        var final = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        final.Status.Should().Be(VoucherStatus.Assigned);
        final.AssignedToUserId.Should().Be(userId);
        final.QrPayload.Should().Be($"qr-{sourceId:N}", "still the same code ΓÇö no new voucher, no new QR");
        final.SupplierExpirationDate.Should().Be(providerTerm);
        final.CustomerExpirationDate.Should().Be(boughtFor.AddDays(7).AddMonths(2),
            "2 months on top of the previous step, not on top of today");

        var (firstLine, firstDelivery) = await LoadRenewalAsync(verify, firstOrderId, sourceId);
        firstDelivery!.VoucherId.Should().Be(sourceId, "Extend points the delivery at the source voucher");
        firstLine.PreviousCustomerExpiration.Should().Be(boughtFor);
        firstLine.NewCustomerExpiration.Should().Be(boughtFor.AddDays(7));

        var (secondLine, secondDelivery) = await LoadRenewalAsync(verify, secondOrderId, sourceId);
        secondDelivery!.VoucherId.Should().Be(sourceId);
        secondLine.PreviousCustomerExpiration.Should().Be(boughtFor.AddDays(7));
        secondLine.NewCustomerExpiration.Should().Be(boughtFor.AddDays(7).AddMonths(2));

        // Two extensions must consume no stock at all.
        (await verify.FuelVouchers.AsNoTracking().AnyAsync(v => v.Status == VoucherStatus.Assigned && v.Id != sourceId))
            .Should().BeFalse("extend never hands over a different voucher");
    }

    /// <summary>
    /// The rule that decides WHICH stock voucher goes out: not "today + term", but the promise ΓÇö the
    /// customer's leftover term plus what they just paid for ΓÇö and among everything that reaches that
    /// date, the SHORTEST one.
    ///
    /// The concrete case the owner described: on 13.03 a customer holds fuel until 27.03 and pays for
    /// 2 months, so they are owed until 27.05. A 2-month voucher (good to 13.05) misses by two weeks and
    /// must be rejected; the 3-month one (13.06) qualifies, and handing that over keeps Fuel Flow's
    /// near-expiry stock from lapsing unused. The customer still only gets until 27.05 ΓÇö the extra real
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

            // Customer term == brand term: no room to grow, so the branch is Replace.
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m,
                sourceExpiry, providerExpiry: sourceExpiry));
            seed.FuelVouchers.Add(StockVoucher(twoMonthsId, "OKKO", "okko-95", 50m, today.AddMonths(2)));
            seed.FuelVouchers.Add(StockVoucher(threeMonthsId, "OKKO", "okko-95", 50m, today.AddMonths(3)));
            seed.FuelVouchers.Add(StockVoucher(sixMonthsId, "OKKO", "okko-95", 50m, today.AddMonths(6)));
            seed.Orders.Add(RenewalOrder(orderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "2m");

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        promise.Should().BeAfter(today.AddMonths(2), "the promise is measured from the customer's own date, so the 2-month voucher is short");
        promise.Should().BeBefore(today.AddMonths(3), "which is exactly why the 3-month voucher is the shortest that qualifies");

        var (qualifiedLine, qualifiedDelivery) = await LoadRenewalAsync(verify, orderId, sourceId);
        qualifiedDelivery!.VoucherId.Should().Be(threeMonthsId, "shortest qualifying voucher, not the longest");

        var issued = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == threeMonthsId);
        issued.Status.Should().Be(VoucherStatus.Assigned);
        issued.CustomerExpirationDate.Should().Be(promise,
            "the customer is given what they paid for on top of what they still held ΓÇö the rest is reserve");
        issued.SupplierExpirationDate.Should().Be(today.AddMonths(3), "the paper term itself is untouched");

        qualifiedLine.PreviousCustomerExpiration.Should().Be(sourceExpiry);
        qualifiedLine.NewCustomerExpiration.Should().Be(promise);

        // The rejected candidates must still be sitting in stock, untouched.
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == twoMonthsId)).Status
            .Should().Be(VoucherStatus.Available, "too short to cover the promise, so not issued");
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sixMonthsId)).Status
            .Should().Be(VoucherStatus.Available, "longer than needed, so preserved for a tier that needs it");
    }

    /// <summary>
    /// When nothing reaches the promise we must NOT ship a short voucher and must NOT quietly clamp the
    /// promised date. The line simply stays open ΓÇö the same posture the no-stock case already had, now
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
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "2m");

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

        var (undeliveredLine, undelivered) = await LoadRenewalAsync(verify, orderId, sourceId);
        undelivered.Should().BeNull();
        undeliveredLine.NewCustomerExpiration.Should().BeNull("and no clamped date was written into the history");

        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.Status.Should().Be(VoucherStatus.Assigned, "the customer keeps what they had");
        source.AssignedToUserId.Should().Be(userId);
        source.CustomerExpirationDate.Should().Be(sourceExpiry);

        (await verify.Fulfillments.AsNoTracking().AnyAsync(f => f.OrderId == orderId)).Should().BeFalse();
    }

    /// <summary>
    /// The voucher a customer gave back is stock again, so the NEXT customer to need fuel at the same
    /// price point gets that same voucher ΓÇö and pays for it. That is what makes the rule harmless: the
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
            MarkAsRenewalLine(seed, firstOrderId, sourceId, termCode: "2m");

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
                        Brand = "OKKO",
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
    /// The released voucher must NOT be shown ΓÇö it is Fuel Flow's stock again and the client has no status
    /// reason to hide an Available voucher. But it must still be LOADED, because resolving a replacement's
    /// origin walks back to the root voucher and reads ITS order_id: filter it out of the lookup and the
    /// replacement resolves to the RENEWAL order, which the wallet does not render ΓÇö the customer's fuel
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
            // puts the source into the customer's voucher chain ΓÇö without it the origin walk has no root to
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
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "2m");

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

            var shown = purchases.SelectMany(p => p.Vouchers ?? []).ToList();
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
    /// so the renewal is done. It must then drop off BOTH the attention list and the badge count ΓÇö not
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
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            // Two lapsed stock vouchers the operator would normally have to renew with the supplier.
            seed.FuelVouchers.Add(StockVoucher(exchangedId, "OKKO", "okko-95", 40m, today.AddDays(-10)));
            seed.FuelVouchers.Add(StockVoucher(stillOwedId, "OKKO", "okko-95", 40m, today.AddDays(-10)));

            // One of them has already been exchanged with the supplier - the confirm handler wrote this row,
            // and it wrote the settlement the row belongs to as well (#319).
            var exchangeBatchId = Guid.NewGuid();
            MovementBatchSeeder.AddForExchange(seed, exchangeBatchId, supplierId, 0m, DateTime.UtcNow);
            seed.VoucherExchanges.Add(new VoucherExchange
            {
                Id = Guid.NewGuid(),
                ExchangeBatchId = exchangeBatchId,
                OldVoucherId = exchangedId,
                FuelTypeId = "okko-95",
                Brand = "OKKO",
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
            "a voucher with a voucher_exchanges row has been renewed with the supplier ΓÇö the job is done");

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

            // One purchase order originally delivered BOTH source vouchers (quantity 2) ΓÇö it is not the
            // renewal order under test, which the service creates and owns on its own.
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            // Line A: still-valid source ΓåÆ Extend, will succeed.
            seed.FuelVouchers.Add(SourceVoucher(extendSourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, today.AddDays(5)));
            // Line B: lapsed source ΓåÆ Replace, but NO matching stock is seeded ΓåÆ stays unfulfilled.
            seed.FuelVouchers.Add(SourceVoucher(replaceSourceId, userId, purchaseOrderId, "WOG", "wog-95", 40m, today.AddDays(-3)));

            seed.Orders.Add(RenewalOrder(orderId, userId, price: 980, monobankInvoiceId: $"test-invoice-{orderId:N}",
                ("OKKO", "okko-95", 50m, 500),
                ("WOG", "wog-95", 40m, 480)));

            MarkAsRenewalLine(seed, orderId, extendSourceId, termCode: "1m");
            MarkAsRenewalLine(seed, orderId, replaceSourceId, termCode: "1m");

            await seed.SaveChangesAsync();
        }

        var monobankMock = new Mock<IMonobankClient>();
        monobankMock
            .Setup(m => m.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()))
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

        var (_, extendApplied) = await LoadRenewalAsync(verify, orderId, extendSourceId);
        extendApplied!.VoucherId.Should().Be(extendSourceId); // the extend line applied

        var (_, replaceUndelivered) = await LoadRenewalAsync(verify, orderId, replaceSourceId);
        replaceUndelivered.Should().BeNull(); // no stock -> left for the backfill / refund

        var refunds = await verify.Payments.AsNoTracking().Where(r => r.OrderId == orderId && r.Kind == PaymentKind.Refund).ToListAsync();
        refunds.Should().ContainSingle(r => r.Status == PaymentStatus.Pending);
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static FulfillmentService BuildService(ApplicationDbContext ctx, IMonobankClient monobankClient)
        => new(
            ctx,
            NullLogger<FulfillmentService>.Instance,
            new RefundOrderCommandHandler(ctx, new StubMonobankClientFactory(monobankClient), new AuditEventService(ctx)),
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
    /// must carry an <c>order_id</c> ΓÇö ck_voucher_held_has_order rejects the row otherwise ΓÇö and for a
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

    /// <summary>
    /// A renewal fee is revenue, and revenue does not change what we paid a supplier.
    /// <para>
    /// This path used to do the opposite: it subtracted the fee from <c>cost_per_liter</c> on every paid
    /// renewal, which made one column mean both "what we paid" and "what this is worth to us now". That
    /// column feeds <c>BlendedCostRecalculator</c>, the package prices and supplier invoicing, so the two
    /// meanings leaked into each other ΓÇö a voucher a customer had fully paid off was priced as if it
    /// were free.
    /// </para>
    /// <para>
    /// The voucher is still bought at 90/L after the customer pays 100 on it. What the fuel is worth now
    /// is a different question, and it belongs in the pricing decision (margin, min discount), not in a
    /// restatement of the purchase price.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ExtendBranch_PaidRenewal_LeavesThePurchaseCostAlone()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sourceExpiry = today.AddDays(5);

        const decimal liters = 10m;
        const decimal originalCost = 90m;
        const int amountPaid = 100;

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            var source = SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", liters, sourceExpiry);
            source.CostPerLiter = originalCost;
            seed.FuelVouchers.Add(source);
            seed.Orders.Add(RenewalOrder(orderId, userId, price: amountPaid, monobankInvoiceId: null,
                ("OKKO", "okko-95", liters, amountPaid)));
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();
        var stored = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);

        // Not 90 ΓêÆ (100 / 10). The fee is income; the purchase price is still the purchase price.
        stored.CostPerLiter.Should().Be(originalCost);
        stored.Status.Should().Be(VoucherStatus.Assigned);
        stored.CustomerExpirationDate.Should().Be(sourceExpiry.AddMonths(1), "the renewal itself still happened");
    }

    /// <summary>
    /// A customer paying far more than the fuel was worth must not touch the cost either ΓÇö not by
    /// flooring it at zero, and certainly not by pushing it negative.
    /// </summary>
    [Fact]
    public async Task ReplaceBranch_Overpayment_LeavesBothVoucherCostsAlone()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        const decimal liters = 10m;
        const decimal sourceCost = 90m;
        const decimal stockCost = 120m;
        const int amountPaid = 5000;       // far more than the fuel is worth

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);
            var source = SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", liters, today.AddDays(-3));
            source.CostPerLiter = sourceCost;
            seed.FuelVouchers.Add(source);

            var stock = SourceVoucher(stockId, userId, Guid.NewGuid(), "OKKO", "okko-95", liters, today.AddMonths(6));
            stock.CostPerLiter = stockCost;
            stock.Status = VoucherStatus.Available;
            stock.AssignedToUserId = null;
            stock.OrderId = null;
            seed.FuelVouchers.Add(stock);

            seed.Orders.Add(RenewalOrder(orderId, userId, price: amountPaid, monobankInvoiceId: null,
                ("OKKO", "okko-95", liters, amountPaid)));
            MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        var replacement = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == stockId);
        replacement.CostPerLiter.Should().Be(stockCost, "the replacement is still worth what we paid for it");

        var released = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        released.CostPerLiter.Should().Be(sourceCost, "the lapsed voucher went back to the pool at its purchase price");
    }

    private static FuelVoucher SourceVoucher(Guid id, Guid userId, Guid orderId, string brand, string fuelTypeId, decimal liters, DateOnly expiry, DateOnly? providerExpiry = null)
        => new()
        {
            Id = id,
            Brand = brand,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            SupplierExpirationDate = providerExpiry ?? expiry.AddYears(1),
            CustomerExpirationDate = expiry,
            VoucherNumber = $"SRC-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            OrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static FuelVoucher StockVoucher(Guid id, string brand, string fuelTypeId, decimal liters, DateOnly expiry)
        => new()
        {
            Id = id,
            Brand = brand,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"STK-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    /// <summary>
    /// Turns a renewal order's line into the renewal line it is supposed to be.
    /// </summary>
    /// <remarks>
    /// Since #1180 the order line carries the renewal's whole intent - source voucher, term, branch -
    /// so a fixture that seeds a renewal order has to say so on the line, not only on
    /// <c>voucher_renewal_items</c>, or it describes an order the application cannot produce.
    /// Matched by order id, which is exact; the source voucher has to be passed in because nothing
    /// else in a fixture knows it.
    /// </remarks>
    private static OrderLineItem MarkAsRenewalLine(
        ApplicationDbContext seed, Guid orderId, Guid sourceVoucherId, string termCode)
    {
        // The first line of this order that is not already a renewal line, so a test with two renewals
        // on one order can mark them in sequence without having to say which is which.
        var line = seed.OrderLineItems.Local.First(l => l.OrderId == orderId && l.SourceVoucherId == null);
        line.SourceVoucherId = sourceVoucherId;
        line.TermCode = termCode;
        line.RenewalBranch = VoucherRenewalBranchCodes.Extend;
        return line;
    }

    private static Order RenewalOrder(
        Guid orderId,
        Guid userId,
        int price,
        string? monobankInvoiceId,
        params (string Brand, string FuelTypeId, decimal Liters, int Amount)[] lines)
    {
        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = price,
            Status = OrderStatus.PendingFulfillment,
            MonobankInvoiceId = monobankInvoiceId,
            // Paid. PendingFulfillment is only ever reached by a `success` webhook, and that
            // handler records MonobankStatus=Success. Stated explicitly because refundable
            // value is now gated on the brand having confirmed the payment - without it an
            // auto-refund silently becomes a no-op and the test passes for the wrong reason.
            MonobankStatus = MonobankStatus.Success,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            LineItems = new List<OrderLineItem>()
        };

        foreach (var (brand, fuelTypeId, liters, amount) in lines)
        {
            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Brand = brand,
                FuelTypeId = fuelTypeId,
                Liters = liters,
                Quantity = 1,
                UnitPrice = amount,
                LineTotal = amount
            });
        }

        return order;
    }

    /// <summary>
    /// A renewal's two owners, loaded together: the line says what was bought and how the term moved,
    /// the fulfilment says whether it has been delivered yet and to which voucher.
    /// </summary>
    /// <remarks>
    /// These were one row on <c>voucher_renewal_items</c> until D-13 dissolved it, and they are
    /// deliberately two here. Asserting through this helper rather than a single object is what makes
    /// the split visible: a green test says the line was extended AND that a fulfilment exists for it,
    /// not that some table has both columns filled in.
    /// </remarks>
    private static async Task<(OrderLineItem Line, Fulfillment? Delivered)> LoadRenewalAsync(
        ApplicationDbContext ctx, Guid orderId, Guid sourceVoucherId)
    {
        var line = await ctx.OrderLineItems.AsNoTracking()
            .SingleAsync(l => l.OrderId == orderId && l.SourceVoucherId == sourceVoucherId);

        // IgnoreQueryFilters deliberately: a released fulfilment still delivered the term, and these
        // tests are about whether delivery happened, not whether the voucher is still held.
        var delivered = await ctx.Fulfillments.AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.LineItemId == line.Id);

        return (line, delivered);
    }

    [Fact]
    public async Task ReleasedFulfilment_OnARenewalLine_StillCountsAsDelivered()
    {
        // The renewal's done-marker used to be `VoucherRenewalItem.FulfilledVoucherId IS NOT NULL`,
        // which had no query filter and so could not see the difference. It is now "this line has a
        // fulfilment", and `Fulfillments` DOES carry a global `released_at_utc IS NULL` filter. This
        // test is the reason both readers of that marker pass `IgnoreQueryFilters`.
        //
        // Nothing releases a renewal fulfilment today - `FixMismatchedFulfillmentsAsync` excludes
        // `Kind == Renewal` (FulfillmentService.cs:104) and the replace path releases the SOURCE
        // voucher's fulfilment, which belongs to the purchase order, not this one. So this is a pin,
        // not a fix for a live bug: it is exactly the kind of "redundant" filter a later refactor
        // would drop, and dropping it makes the retry sweep extend the same voucher a second time.
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var originalExpiry = today.AddDays(5);
        var onceRenewed = originalExpiry.AddMonths(1); // what the first, already-applied extend wrote

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, userId);
            var purchaseOrderId = SeedPurchaseOrder(seed, userId);

            // The voucher as the FIRST renewal left it: still the customer's, expiry already pushed.
            seed.FuelVouchers.Add(SourceVoucher(sourceId, userId, purchaseOrderId, "OKKO", "okko-95", 50m, onceRenewed));

            seed.Orders.Add(RenewalOrder(orderId, userId, price: 500, monobankInvoiceId: null,
                ("OKKO", "okko-95", 50m, 500)));
            var line = MarkAsRenewalLine(seed, orderId, sourceId, termCode: "1m");
            line.PreviousCustomerExpiration = originalExpiry;
            line.NewCustomerExpiration = onceRenewed;

            // The delivery, marked released - the state under test.
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = sourceId,
                LineItemId = line.Id,
                FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-10),
                ReleasedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                ReleaseReason = "quantity_mismatch"
            });

            await seed.SaveChangesAsync();
        }

        // Deliberately left PendingFulfillment, which is what the per-minute retry sweep re-drives.
        using (var ctx = CreateContext())
        {
            await BuildService(ctx, new Mock<IMonobankClient>().Object).ProcessRenewalOrderAsync(orderId);
        }

        using var verify = CreateContext();

        // The point of the test: a second extend would push the expiry out again, and the snapshots
        // would be overwritten with the wrong "previous" value - losing the record of what the
        // customer actually bought.
        var source = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == sourceId);
        source.CustomerExpirationDate.Should().Be(onceRenewed,
            "a released fulfilment still delivered the term, so it must not be delivered twice");

        var (line2, delivered) = await LoadRenewalAsync(verify, orderId, sourceId);
        line2.PreviousCustomerExpiration.Should().Be(originalExpiry);
        line2.NewCustomerExpiration.Should().Be(onceRenewed);

        (await verify.Fulfillments.IgnoreQueryFilters().CountAsync(f => f.LineItemId == line2.Id))
            .Should().Be(1, "no second delivery row for an already-delivered line");

        // And the order still finishes: treating the line as undelivered would leave it open forever,
        // retrying every minute against a voucher's supplier term that no longer has room.
        (await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId)).Status
            .Should().Be(OrderStatus.Fulfilled);
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "voucher_exchanges", "movement_batches", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
