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

        // Margin is a property of the offer, so the lookup is volumes joined to offers, pinned to the
        // real pool: these are operator counters over real business, and the old lookup had no pool
        // filter, so a QA rehearsal row could shadow a real one by being newer.
        //
        // The GroupBy(...).First() this replaces existed only to break ties the schema already
        // forbids - UNIQUE (brand_id, fuel_type_id, data_kind) on fuel_offers times
        // UNIQUE (offer_id, liters) on fuel_offer_volumes makes the triple unique inside a pool. A
        // duplicate now throws rather than silently resolving to one arbitrary price.
        // Projects the margin, not the offer entity. Handing back a whole tracked-free entity and then
        // reading one column off it loads every offer column for every volume in the catalogue to
        // answer one question, and - worse - depends on the provider materialising the navigation,
        // which the in-memory provider does not and which would make this throw on a null rather than
        // return a wrong number.
        // An explicit join rather than `v.Offer.X`: relying on the navigation being materialised is a
        // silent dependency on provider behaviour, and where it is not materialised this throws on a
        // null instead of returning a wrong number - which is exactly the kind of failure that looks
        // like a data problem and gets debugged as one.
        //
        // Projects the margin alone. Handing back the offer entity would load every offer column for
        // every volume in the catalogue to answer one question.
        var fpLookup = await (
            from v in _context.FuelOfferVolumes.AsNoTracking()
            join o in _context.FuelOffers.AsNoTracking() on v.OfferId equals o.Id
            // The margin is the fuel's, so the fuel joins too - see GetDashboardQueryHandler, the same
            // lookup answered one number.
            join f in _context.FuelTypes.AsNoTracking() on o.FuelTypeId equals f.Id
            where o.DataKind == DataKinds.Real
            select new { o.BrandId, o.FuelTypeId, v.Liters, MarginUahPerLiter = f.MarginUahPerLiter })
            .ToDictionaryAsync(
                x => (x.BrandId, x.FuelTypeId, x.Liters),
                x => x.MarginUahPerLiter,
                cancellationToken);

        // Closed orders (incl. refunded) form the revenue set; margin is earned only on
        // delivered liters, so refunded liters drop out of revenue automatically. A company
        // handover is excluded here — it never earned us revenue — while the three-way-match
        // list below still shows it, because an auditor must be able to see the handover.
        var fulfilledOrders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .Where(o => (o.Status == OrderStatus.Fulfilled
                || o.Status == OrderStatus.PartiallyFulfilled
                || o.Status == OrderStatus.PartiallyRefunded
                || o.Status == OrderStatus.Refunded)
                && o.Kind != OrderKind.ReceivedFromCompany)
            .ToListAsync(cancellationToken);

        decimal EarnedMarginKopecks(Order o)
        {
            decimal sum = 0m;
            foreach (var g in o.LineItems.GroupBy(li => (li.Brand, li.FuelTypeId, li.Liters)))
            {
                if (!fpLookup.TryGetValue(g.Key, out var margin) || margin is null)
                {
                    continue;
                }

                var delivered = o.Fulfillments.Count(f => f.Voucher is not null
                    && string.Equals(f.Voucher.Brand, g.Key.Brand, StringComparison.OrdinalIgnoreCase)
                    && f.Voucher.FuelTypeId == g.Key.FuelTypeId
                    && f.Voucher.Liters == g.Key.Liters);

                // No cast: `sum` is decimal now, and this figure is kopecks. A `(long)` here would
                // truncate the half-kopecks a fractional margin per litre produces - the cast existed
                // only because the column this reads from was an integer type.
                sum += margin.Value * (decimal)g.Key.Liters * Math.Min(g.Sum(li => li.Quantity), delivered);
            }

            return sum;
        }

        var revenue = fulfilledOrders.Sum(EarnedMarginKopecks);

        var orphanVouchers = await _context.FuelVouchers
            .CountAsync(v => v.Status == VoucherStatus.Assigned
                && !_context.Fulfillments.Any(f => f.VoucherId == v.Id), cancellationToken);

        var unprocessed = await _context.OutboxEvents.CountAsync(e => !e.Processed, cancellationToken);

        var importErrors = await _context.VoucherImportErrors
            .CountAsync(e => e.CreatedAtUtc >= DateTime.UtcNow.AddDays(-7), cancellationToken);

        var lowInventoryProviders = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
            .SelectMany(o => o.LineItems.Select(li => li.Brand))
            .Distinct()
            .CountAsync(cancellationToken);

        var orders = await _context.Orders
            .AsNoTracking()
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
                firstLi?.FuelTypeId ?? "",
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

        var revenueRaw = fulfilledOrders
            .GroupBy(o => new { o.CreatedAtUtc.Year, o.CreatedAtUtc.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                OrderCount = g.Count(),
                RevenueKopecks = g.Sum(EarnedMarginKopecks)
            })
            .ToList();

        var revenueSummary = revenueRaw
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .Select(x => new GetReconciliationResponse.RevenueSummaryItem(
                x.Year, x.Month, x.OrderCount, x.RevenueKopecks))
            .ToList();

        var summary = new GetReconciliationResponse.SummaryData(
            totalOrders, paidUnfulfilled, partiallyFulfilled, fulfilled,
            revenue, orphanVouchers, unprocessed, lowInventoryProviders, importErrors,
            orders.Count(o => o.Status == OrderStatus.PartiallyRefunded || o.Status == OrderStatus.Refunded),
            orders.Where(o => o.MonobankStatus == MonobankStatus.Success).Sum(o => Money.ToKopecks(o.Price)),
            orders.Sum(o => RefundOrderCommandHandler.ComputeFulfilledValueKopecks(o)),
            refundsByOrder.Values.Where(p => p.Status == PaymentStatus.Succeeded).Sum(p => p.AmountKopecks));

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