using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Admin.GetReconciliation;

public sealed class GetReconciliationQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetReconciliationQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetReconciliationResponse> HandleAsync(
        GetReconciliationQuery query,
        CancellationToken cancellationToken = default)
    {
        var totalOrders = await _context.Orders.CountAsync(cancellationToken);
        var fulfilled = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Fulfilled, cancellationToken);
        var paidUnfulfilled = await _context.Orders.CountAsync(o => o.Status == OrderStatus.PendingFulfillment, cancellationToken);
        var partiallyFulfilled = await _context.Orders.CountAsync(o => o.Status == OrderStatus.PartiallyFulfilled, cancellationToken);

        // THE REVENUE, SUMMED BY THE DATABASE.
        //
        // This used to load every closed order in the platform with its line items, its
        // fulfillments and its vouchers, and add the margin up in C#. A read that grows with
        // every sale, for numbers that are aggregates.
        //
        // WHAT THE NUMBERS MEAN. The margin is earned on DELIVERED liters: a group of lines
        // (brand, fuel, litres) earns its margin on the lesser of what was ordered and what
        // was delivered. A refunded order's undelivered liters therefore earn nothing, which
        // is what "refunded liters drop out of revenue automatically" means.
        //
        // THE MARGIN LOOKUP, STILL A LOOKUP AND NOT A NAVIGATION. The margin lives on the
        // FUEL TYPE, and the size is a row in fuel_nominals - whose key (fuel_type_id,
        // data_kind, litres) is one row per fuel, pool and size - so this join cannot match
        // twice and cannot silently pick one of two prices. Pinned to the real pool: these are
        // operator counters over real business, and the old lookup had no pool filter, so a QA
        // rehearsal row could shadow a real one by being newer. The line names the brand and
        // the fuel type belongs to one, so a line whose brand and fuel type disagree has no
        // margin - and the old lookup said the same, and answered zero.
        //
        // The groups are joined to the delivered ones, and a group with no delivery earns
        // nothing, so it does not need to be in the result: joining it away changes no sum.
        var revenueByMonth = await (
            from o in _context.Orders.AsNoTracking()
            join lg in _context.OrderLineItems.AsNoTracking()
                .GroupBy(li => new { li.OrderId, li.Brand, li.FuelTypeId, li.Liters })
                .Select(g => new
                {
                    g.Key.OrderId,
                    g.Key.Brand,
                    g.Key.FuelTypeId,
                    g.Key.Liters,
                    OrderedQty = g.Sum(li => li.Quantity)
                })
            on o.Id equals lg.OrderId
            join d in _context.Fulfillments.AsNoTracking()
                .Where(f => f.VoucherId != null && f.ReleasedAtUtc == null)
                .Select(f => new { f.OrderId, f.Voucher!.Brand, f.Voucher.FuelTypeId, f.Voucher.Liters })
                .GroupBy(x => new { x.OrderId, x.Brand, x.FuelTypeId, x.Liters })
                .Select(g => new
                {
                    g.Key.OrderId,
                    g.Key.Brand,
                    g.Key.FuelTypeId,
                    g.Key.Liters,
                    DeliveredQty = g.Count()
                })
            on new { lg.OrderId, lg.Brand, lg.FuelTypeId, lg.Liters }
                equals new { d.OrderId, d.Brand, d.FuelTypeId, d.Liters }
            join f in _context.FuelTypes.AsNoTracking() on lg.FuelTypeId equals f.Id
            join b in _context.Brands.AsNoTracking() on f.BrandId equals b.Id
            join n in _context.FuelNominals.AsNoTracking()
                on new { FuelTypeId = f.Id, Liters = lg.Liters } equals new { n.FuelTypeId, n.Liters }
            where (o.Status == OrderStatus.Fulfilled
                    || o.Status == OrderStatus.PartiallyFulfilled
                    || o.Status == OrderStatus.PartiallyRefunded
                    || o.Status == OrderStatus.Refunded)
                // A company handover is excluded here - it never earned us revenue - while the
                // three-way-match list below still shows it, because an auditor must be able to
                // see the handover.
                && o.Kind != OrderKind.ReceivedFromCompany
                && n.DataKind == DataKinds.Real
                && b.Slug == lg.Brand
            group new { o, lg, d, f } by new { o.CreatedAtUtc.Year, o.CreatedAtUtc.Month } into g
            select new
            {
                g.Key.Year,
                g.Key.Month,
                RevenueUah = g.Sum(x => (x.f.MarginUahPerLiter ?? 0m) * x.lg.Liters
                    * Math.Min(x.lg.OrderedQty, x.d.DeliveredQty))
            })
            .ToListAsync(cancellationToken);

        // The total is the sum of the months, which is the same number as the sum over the
        // orders: the margin is a decimal and the additions are exact, so the grouping does
        // not change the figure - it only decides how many rows the database has to add up.
        //
        // It is UAH, and it is called UAH: the margin is UAH per litre, so the product is
        // UAH. A field that says "kopecks" and holds UAH is a number that lies about itself
        // in a summary of money.
        var revenueUah = revenueByMonth.Sum(m => m.RevenueUah);


        var orphanVouchers = await _context.FuelVouchers
            .CountAsync(v => v.Status == VoucherStatus.Assigned
                && !_context.Fulfillments.Any(f => f.VoucherId == v.Id), cancellationToken);

        // The events that are waiting for a CONSUMER, not every event that is unprocessed. Five of
        // the eight event types have no consumer and are never marked processed - they are the
        // record of something that happened, and they are most of the table. Counting them here
        // made the number thousands instead of units, so the "fulfillment jobs may be stalled"
        // alarm below was always on, which is the same as no alarm at all.
        var unprocessed = await _context.OutboxEvents.CountAsync(
            e => !e.Processed && ConsumedOutboxEventTypes.All.Contains(e.EventType),
            cancellationToken);

        var importErrors = await _context.VoucherImportErrors
            .CountAsync(e => e.CreatedAtUtc >= DateTime.UtcNow.AddDays(-7), cancellationToken);

        var lowInventoryProviders = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
            .SelectMany(o => o.LineItems.Select(li => li.Brand))
            .Distinct()
            .CountAsync(cancellationToken);

        var orders = await _context.Orders
            .AsNoTracking()
            // Same cross product as above: LineItems x Fulfillments.
            .AsSplitQuery()
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .Include(o => o.LineItems)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var allFulfillments = await _context.Fulfillments
            .Where(f => orderIds.Contains(f.OrderId))
            .ToListAsync(cancellationToken);
        var fulfillByOrder = allFulfillments.GroupBy(f => f.OrderId).ToDictionary(g => g.Key, g => g.Count());

        var refundsByOrder = await _context.Payments
            .AsNoTracking()
// A refund reverses a capture, and a capture always has an order. Stated as a filter rather than a
                // null-forgiving cast so the guarantee is visible: an orderless payment is money we hold,
                // not something a statement line can be reconciled against.
                .Where(p => p.Kind == PaymentKind.Refund && p.OrderId != null && orderIds.Contains(p.OrderId.Value))
                .ToDictionaryAsync(p => p.OrderId!.Value, cancellationToken);

        var threeWayMatch = orders.Select(o =>
        {
            var lineItemsList = o.LineItems.ToList();
            var firstLi = lineItemsList.FirstOrDefault();
            var expected = lineItemsList.Sum(li => li.Quantity);
            var delivered = fulfillByOrder.GetValueOrDefault(o.Id, 0);
            var matchStatus = (o.Status == OrderStatus.Fulfilled && delivered >= expected) ? "OK"
                : o.Status == OrderStatus.Cancelled ? "CANCELLED"
                : o.Status == OrderStatus.Refunded ? "REFUNDED"
                : o.Status == OrderStatus.PartiallyRefunded ? "PARTIAL_REFUNDED"
                : (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.Paid) ? "AWAITING_PAYMENT"
                : delivered == 0 ? "UNFULFILLED"
                : delivered < expected ? "PARTIAL"
                : "OK";

            refundsByOrder.TryGetValue(o.Id, out var refund);

            return new GetReconciliationResponse.ThreeWayMatchItem(
                o.Id,
                null,
                firstLi?.Brand ?? "",
                firstLi?.FuelTypeId ?? Guid.Empty,
                lineItemsList.Sum(li => li.Liters * li.Quantity),
                expected,
                o.Price,
                o.Status.ToString(),
                o.MonobankStatus?.ToString(),
                expected,
                delivered,
                matchStatus,
                (DateTime.UtcNow - o.CreatedAtUtc).Days,
                o.CreatedAtUtc,
                refund is null ? null : RefundStatusWord(refund.Status),
                refund is { Status: PaymentStatus.Succeeded } ? refund.AmountKopecks : 0L);
        }).ToList();

        var exceptions = new List<GetReconciliationResponse.ExceptionItem>();

        foreach (var o in threeWayMatch)
        {
            if (o.MatchStatus == "UNFULFILLED" && o.MonobankStatus == "Success")
                exceptions.Add(new(o.OrderId, "UNPAID_FULFILLMENT",
                    $"Order paid ({o.TotalPrice} UAH) but no vouchers assigned — {o.DaysSinceCreated}d old",
                    "critical", o.CreatedAtUtc));
            else if (o.MatchStatus == "UNFULFILLED")
                exceptions.Add(new(o.OrderId, "UNFULFILLED",
                    $"{o.Brand} {o.FuelType} {o.Liters}L×{o.Quantity} — no inventory ({o.DaysSinceCreated}d waiting)",
                    "warning", o.CreatedAtUtc));
            else if (o.MatchStatus == "PARTIAL")
                exceptions.Add(new(o.OrderId, "PARTIAL",
                    $"Only {o.VouchersDelivered}/{o.VouchersExpected} vouchers delivered for {o.Brand} {o.FuelType}",
                    "warning", o.CreatedAtUtc));
        }

        if (orphanVouchers > 0)
            exceptions.Add(new(Guid.Empty, "ORPHAN",
                $"{orphanVouchers} voucher(s) marked Assigned but linked to no fulfillment record",
                "critical", DateTime.UtcNow));

        if (unprocessed > 10)
            exceptions.Add(new(Guid.Empty, "OUTBOX",
                $"{unprocessed} unprocessed outbox events — fulfillment jobs may be stalled",
                "critical", DateTime.UtcNow));

        var funnelRaw = await _context.FuelVouchers
            .AsNoTracking()
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), TotalLiters = g.Sum(v => v.Liters) })
            .ToListAsync(cancellationToken);

        var funnel = funnelRaw
            .Select(x => new GetReconciliationResponse.VoucherFunnelItem(
                x.Status.ToString(), x.Count, x.TotalLiters))
            .ToList();

        // The revenue summary: the same months, with the order count beside the money. The
        // count is its own aggregate - the number of closed orders in the month, whether or
        // not they earned a margin - because "how many orders" and "how much margin" are
        // different questions, and a month with orders but no margin still belongs in the
        // table.
        var orderCountByMonth = await _context.Orders
            .AsNoTracking()
            .Where(o => (o.Status == OrderStatus.Fulfilled
                         || o.Status == OrderStatus.PartiallyFulfilled
                         || o.Status == OrderStatus.PartiallyRefunded
                         || o.Status == OrderStatus.Refunded)
                     && o.Kind != OrderKind.ReceivedFromCompany)
            .GroupBy(o => new { o.CreatedAtUtc.Year, o.CreatedAtUtc.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, OrderCount = g.Count() })
            .ToListAsync(cancellationToken);

        var revenueSummary = orderCountByMonth
            .Select(m => new
            {
                m.Year,
                m.Month,
                m.OrderCount,
                RevenueUah = revenueByMonth
                    .Where(r => r.Year == m.Year && r.Month == m.Month)
                    .Select(r => r.RevenueUah)
                    .FirstOrDefault()
            })
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Month)
            .Select(x => new GetReconciliationResponse.RevenueSummaryItem(
                x.Year, x.Month, x.OrderCount, x.RevenueUah))
            .ToList();

        // THE MONEY LEDGER, SUMMED BY THE DATABASE.
        //
        // The three numbers the screen puts at the top - received, delivered, refunded - are
        // aggregates over the whole history, and they are now the database's arithmetic
        // rather than the process's. A number that describes the whole business must not be
        // computed from the rows a screen happens to be holding.
        //
        // ROUNDING. `Money.ToKopecks` rounds a UAH figure to whole kopecks, half away from
        // zero, and `floor(x * 100 + 0.5)` is that rule for a non-negative amount - the same
        // rule PostgreSQL's own `round()` uses for a numeric, and the one the money path
        // applies at checkout, so a domain amount and its stored image cannot disagree by a
        // rounding step. It is applied PER ORDER, exactly as it was: the total is the sum of
        // the rounded orders, not the rounding of the total.
        var totalReceivedKopecks = await _context.Orders
            .AsNoTracking()
            .Where(o => o.MonobankStatus == MonobankStatus.Success)
            .SumAsync(o => Math.Floor(o.Price * 100m + 0.5m), cancellationToken);

        // The delivered value: what the order was worth, less what the customer never
        // received and is owed back.
        //
        // The unfulfilled part is per (brand, fuel, litres) group, the same grouping the
        // refund path uses: the ordered units minus the DELIVERED ones - a released delivery is
        // not a delivery the customer received - priced at the group's highest unit price. A
        // company handover never captured money, so it has no unfulfilled value to refund; and
        // an order the brand never confirmed as paid is worth nothing here at all.
        var deliveredValueKopecks = await _context.Orders
            .AsNoTracking()
            .Where(o => o.MonobankStatus == MonobankStatus.Success)
            .SumAsync(o => Math.Floor(o.LineItems.Sum(li => li.UnitPrice * li.Quantity) * 100m + 0.5m)
                - (o.Kind == OrderKind.ReceivedFromCompany
                    ? 0m
                    : Math.Floor(
                        o.LineItems
                            .GroupBy(li => new { li.Brand, li.FuelTypeId, li.Liters })
                            .Select(g => new
                            {
                                g.Key,
                                Ordered = g.Sum(li => li.Quantity),
                                Delivered = _context.Fulfillments.Count(f =>
                                    f.VoucherId != null
                                    && f.OrderId == o.Id
                                    && f.ReleasedAtUtc == null
                                    && f.Voucher!.Brand == g.Key.Brand
                                    && f.Voucher.FuelTypeId == g.Key.FuelTypeId
                                    && f.Voucher.Liters == g.Key.Liters),
                                UnitPrice = g.Max(li => li.UnitPrice)
                            })
                            .Sum(x => Math.Max(x.Ordered - x.Delivered, 0) * x.UnitPrice)
                        * 100m + 0.5m)),
                cancellationToken);

        // The refunded total: the successful refunds that belong to an order. A refund with no
        // order is money we hold, not money we gave back against a sale, and it is not part
        // of what a customer was refunded.
        var totalRefundedKopecks = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Kind == PaymentKind.Refund
                     && p.OrderId != null
                     && p.Status == PaymentStatus.Succeeded)
            .SumAsync(p => p.AmountKopecks, cancellationToken);

        var refundedOrders = await _context.Orders
            .CountAsync(o => o.Status == OrderStatus.PartiallyRefunded || o.Status == OrderStatus.Refunded,
                cancellationToken);

        var summary = new GetReconciliationResponse.SummaryData(
            totalOrders, paidUnfulfilled, partiallyFulfilled, fulfilled,
            revenueUah, orphanVouchers, unprocessed, lowInventoryProviders, importErrors,
            refundedOrders,
            totalReceivedKopecks,
            deliveredValueKopecks,
            totalRefundedKopecks);

        return new GetReconciliationResponse(summary, threeWayMatch, exceptions, funnel, revenueSummary);
    }

    /// <summary>
    /// A refund payment's status, spelled the way these reports have always spelled it.
    /// </summary>
    /// <remarks>
    /// The reports carry a refund status string that the frontend and exports have been reading for
    /// years, keyed on <c>Processing</c> / <c>Completed</c> / <c>Failed</c>. <see cref="PaymentStatus"/>
    /// says the same three things under different names, so the vocabulary is mapped here rather than
    /// every consumer's expectations being rewritten.
    /// </remarks>
    private static string RefundStatusWord(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Processing",
        PaymentStatus.Succeeded => "Completed",
        _ => "Failed"
    };
}
