using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
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
                && o.Status != OrderStatus.PendingPayment);

        if (query.UserId.HasValue && query.UserId != Guid.Empty)
            ordersQuery = ordersQuery.Where(o => o.UserId == query.UserId.Value);

        var orders = await ordersQuery
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var refundsByOrder = await _context.Refunds
            .AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId))
            .ToDictionaryAsync(r => r.OrderId, cancellationToken);

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

        var fuelPackages = await _context.FuelPackages
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var fpLookup = fuelPackages
            .GroupBy(fp => (fp.StationId, fp.FuelTypeId, fp.Liters))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(fp => fp.PriceUpdatedAt ?? fp.CreatedAtUtc).First());

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
                firstLi?.Provider ?? "",
                firstLi?.FuelTypeId ?? "",
                firstLi is null ? "" : fuelTypeNames.GetValueOrDefault(firstLi.FuelTypeId) ?? firstLi.FuelTypeId,
                totalLiters,
                totalQuantity,
                o.MonobankStatus?.ToString(),
                o.MonobankInvoiceId,
                RefundOrderCommandHandler.ComputeFulfilledValueKopecks(o),
                refund is { Status: RefundStatus.Completed } ? refund.Amount : 0,
                refund?.Status.ToString()
            );
        }).ToList();

        var redemptions = usedVouchers.Select(v => new RedemptionEntry(
            v.Id,
            v.Provider,
            v.FuelTypeId,
            fuelTypeNames.GetValueOrDefault(v.FuelTypeId) ?? v.FuelTypeId,
            v.Liters,
            v.UpdatedAtUtc
        )).ToList();

        // Ledger semantics: margin is earned only on liters actually delivered via
        // vouchers; refunded or still-undelivered liters contribute no profit.
        long GetProfit(Order o)
        {
            var groups = o.LineItems.GroupBy(li => (li.Provider, li.FuelTypeId, li.Liters));
            long sum = 0;

            foreach (var g in groups)
            {
                if (!fpLookup.TryGetValue(g.Key, out var fp) || fp.MarginUahPerLiter is null)
                {
                    continue;
                }

                var delivered = o.Fulfillments.Count(f => f.Voucher is not null
                    && string.Equals(f.Voucher.Provider, g.Key.Provider, StringComparison.OrdinalIgnoreCase)
                    && f.Voucher.FuelTypeId == g.Key.FuelTypeId
                    && f.Voucher.Liters == g.Key.Liters);

                var ordered = g.Sum(li => li.Quantity);
                sum += (long)(fp.MarginUahPerLiter.Value * (decimal)g.Key.Liters * Math.Min(ordered, delivered));
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
            orders.Sum(o => (long)RefundOrderCommandHandler.ComputeFulfilledValueKopecks(o)),
            refundsByOrder.Values.Where(r => r.Status == RefundStatus.Completed).Sum(r => (long)r.Amount)
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
                completedRefund.Status == RefundStatus.Completed)
            {
                var refundMonth = o.CreatedAtUtc.ToString("yyyy-MM");
                if (monthlyGroups.TryGetValue(refundMonth, out var existing))
                {
                    monthlyGroups[refundMonth] = existing with
                    {
                        TotalRefundedKopecks = existing.TotalRefundedKopecks + completedRefund.Amount
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
}
