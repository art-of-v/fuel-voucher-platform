using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Report.GetReport;

public sealed class GetReportQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetReportQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetReportResponse> HandleAsync(
        GetReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var fromDate = query.FromDate ?? DateTime.MinValue;
        var toDate = query.ToDate ?? DateTime.MaxValue;

        var ordersQuery = _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .Where(o => o.CreatedAtUtc >= fromDate
                && o.CreatedAtUtc <= toDate
                && o.Status != OrderStatus.PendingPayment
                // A company handing already-purchased fuel to a worker is not a sale: Price is 0 and
                // the line items carry its internal cost, so it must move none of this query's money
                // or counters. Filtering here keeps every aggregation below honest at once.
                && o.Kind != OrderKind.ReceivedFromCompany);

        if (query.UserId.HasValue && query.UserId != Guid.Empty)
            ordersQuery = ordersQuery.Where(o => o.UserId == query.UserId.Value);

        var orders = await ordersQuery
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var refundsByOrder = await _context.Payments
            .AsNoTracking()
// A refund reverses a capture, and a capture always has an order - so the null check is not
                // defensive noise, it is what keeps an orderless payment out of a dictionary keyed by
                // order id. It also means an unallocated payment can never leak into a per-order
                // revenue report, which is the whole point of holding it as an advance.
                .Where(p => p.Kind == PaymentKind.Refund && p.OrderId != null && orderIds.Contains(p.OrderId.Value))
                .ToDictionaryAsync(p => p.OrderId!.Value, cancellationToken);

        var vouchersQuery = _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Used
                && v.UpdatedAtUtc >= fromDate
                && v.UpdatedAtUtc <= toDate);

        if (query.UserId.HasValue && query.UserId != Guid.Empty)
            vouchersQuery = vouchersQuery.Where(v => v.AssignedToUserId == query.UserId.Value);

        var usedVouchers = await vouchersQuery
            .OrderByDescending(v => v.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var allFuelTypeIds = orders
            .SelectMany(o => o.LineItems.Select(li => li.FuelTypeId))
            .Concat(usedVouchers.Select(v => v.FuelTypeId))
            .Distinct()
            .ToList();

        var fuelTypeNames = await _context.FuelTypes
            .Where(ft => allFuelTypeIds.Contains(ft.Id))
            .ToDictionaryAsync(ft => ft.Id, ft => ft.Name, cancellationToken);

        // Margin comes from the offer, so this reads volumes joined to their offer. The real pool is chosen
        // deliberately: this is an operator report over real business, and the old lookup had no pool
        // filter at all, so a QA rehearsal row could shadow a real one purely by being newer.
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
    from o in _context.FuelOffers.AsNoTracking()
            // The margin is the fuel's, so the fuel joins too - see GetDashboardQueryHandler.
            join f in _context.FuelTypes.AsNoTracking() on o.FuelTypeId equals f.Id
    // Keyed the way the consumer keys it: an order line stores the network as its slug, so the
    // lookup is built from the slug rather than from the offer's uuid.
    join b in _context.Brands.AsNoTracking() on o.BrandId equals b.Id
    from n in _context.FuelNominals.AsNoTracking() where n.FuelTypeId == f.Id
    where o.DataKind == DataKinds.Real
    select new { BrandSlug = b.Slug, o.FuelTypeId, Liters = n.Liters, MarginUahPerLiter = f.MarginUahPerLiter })
            .ToDictionaryAsync(
                x => (x.BrandSlug, x.FuelTypeId, x.Liters),
                x => x.MarginUahPerLiter,
                cancellationToken);

        // No GroupBy(...).First() any more, and that is the point of the split. The triple used to have
        // several candidate rows and this picked one "most recently priced" - which, across the real and
        // QA pools, silently read the rehearsal price for a real order. It is now a genuine key:
        // `UNIQUE (brand_id, fuel_type_id, data_kind)` on fuel_offers times
        // `UNIQUE (offer_id, liters)` on fuel_offer_volumes, with the pool pinned above. Two rows cannot
        // collide, so there is nothing left to arbitrate - and a duplicate here would now throw instead
        // of quietly picking a price.

        var period = new ReportPeriod(
            query.FromDate,
            query.ToDate
        );

        var payments = orders.Select(o =>
        {
            var firstLi = o.LineItems.FirstOrDefault();
            var totalLiters = o.LineItems.Sum(li => li.Liters * li.Quantity);
            var totalQuantity = o.LineItems.Sum(li => li.Quantity);
            refundsByOrder.TryGetValue(o.Id, out var refund);
            return new PaymentEntry(
                o.Id,
                o.Price,
                o.Status.ToString(),
                o.CreatedAtUtc,
                firstLi?.Brand ?? "",
                firstLi?.FuelTypeId ?? "",
                firstLi is null ? "" : fuelTypeNames.GetValueOrDefault(firstLi.FuelTypeId) ?? firstLi.FuelTypeId,
                totalLiters,
                totalQuantity,
                o.MonobankStatus?.ToString(),
                o.MonobankInvoiceId,
                RefundOrderCommandHandler.ComputeFulfilledValueKopecks(o),
                refund is { Status: PaymentStatus.Succeeded } ? refund.AmountKopecks : 0,
                refund is null ? null : RefundStatusWord(refund.Status)
            );
        }).ToList();

        var redemptions = usedVouchers.Select(v => new RedemptionEntry(
            v.Id,
            v.Brand,
            v.FuelTypeId,
            fuelTypeNames.GetValueOrDefault(v.FuelTypeId) ?? v.FuelTypeId,
            v.Liters,
            v.UpdatedAtUtc
        )).ToList();

        // Ledger semantics: margin is earned only on liters actually delivered via
        // vouchers; refunded or still-undelivered liters contribute no profit.
        decimal GetProfit(Order o)
        {
            var groups = o.LineItems.GroupBy(li => (li.Brand, li.FuelTypeId, li.Liters));
            decimal sum = 0m;

            foreach (var g in groups)
            {
                if (!fpLookup.TryGetValue(g.Key, out var margin) || margin is null)
                {
                    continue;
                }

                var delivered = o.Fulfillments.Count(f => f.Voucher is not null
                    && string.Equals(f.Voucher.Brand, g.Key.Brand, StringComparison.OrdinalIgnoreCase)
                    && f.Voucher.FuelTypeId == g.Key.FuelTypeId
                    && f.Voucher.Liters == g.Key.Liters);

                var ordered = g.Sum(li => li.Quantity);
                // No cast: `sum` is decimal now, and this figure is kopecks. A `(long)` here would
                // truncate the half-kopecks a fractional margin per litre produces - the cast existed
                // only because the column this reads from was an integer type.
                sum += margin.Value * (decimal)g.Key.Liters * Math.Min(ordered, delivered);
            }

            return sum;
        }

        var summary = new ReportSummary(
            orders.Sum(GetProfit),
            orders.Count,
            orders.Sum(o => o.LineItems.Sum(li => li.Quantity)),
            usedVouchers.Count,
            orders.Sum(o => o.LineItems.Sum(li => li.Liters * li.Quantity)),
            usedVouchers.Sum(v => v.Liters),
            orders.Where(o => o.MonobankStatus == MonobankStatus.Success).Sum(o => Money.ToKopecks(o.Price)),
            orders.Sum(o => RefundOrderCommandHandler.ComputeFulfilledValueKopecks(o)),
            refundsByOrder.Values.Where(p => p.Status == PaymentStatus.Succeeded).Sum(p => p.AmountKopecks)
        );

        var monthlyGroups = orders
            .GroupBy(o => o.CreatedAtUtc.ToString("yyyy-MM"))
            .Select(g => new MonthlyBreakdown(
                g.Key,
                g.Sum(GetProfit),
                g.Sum(o => o.LineItems.Sum(li => li.Quantity)),
                0,
                g.Sum(o => o.LineItems.Sum(li => li.Liters * li.Quantity)),
                0,
                0
            ))
            .ToDictionary(b => b.Month);

        foreach (var o in orders)
        {
            if (refundsByOrder.TryGetValue(o.Id, out var completedRefund) &&
                completedRefund.Status == PaymentStatus.Succeeded)
            {
                var refundMonth = o.CreatedAtUtc.ToString("yyyy-MM");
                if (monthlyGroups.TryGetValue(refundMonth, out var existing))
                {
                    monthlyGroups[refundMonth] = existing with
                    {
                        TotalRefundedKopecks = existing.TotalRefundedKopecks + completedRefund.AmountKopecks
                    };
                }
            }
        }

        foreach (var v in usedVouchers)
        {
            var month = v.UpdatedAtUtc.ToString("yyyy-MM");
            if (monthlyGroups.TryGetValue(month, out var existing))
            {
                monthlyGroups[month] = existing with
                {
                    VouchersUsed = existing.VouchersUsed + 1,
                    TotalLitersUsed = existing.TotalLitersUsed + v.Liters
                };
            }
            else
            {
                monthlyGroups[month] = new MonthlyBreakdown(
                    month,
                    0,
                    0,
                    1,
                    0,
                    v.Liters,
                    0
                );
            }
        }

        var monthlyBreakdown = monthlyGroups.Values
            .OrderBy(b => b.Month)
            .ToList();

        return new GetReportResponse(
            period,
            summary,
            payments,
            redemptions,
            monthlyBreakdown
        );
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
