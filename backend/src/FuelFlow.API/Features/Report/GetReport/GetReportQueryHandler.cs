using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
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
            .Where(o => o.CreatedAtUtc >= fromDate
                && o.CreatedAtUtc <= toDate
                && o.Status != OrderStatus.PendingPayment);

        if (query.UserId.HasValue && query.UserId != Guid.Empty)
            ordersQuery = ordersQuery.Where(o => o.UserId == query.UserId.Value);

        var orders = await ordersQuery
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

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
            return new PaymentEntry(
                o.Id,
                o.Price,
                o.Status.ToString(),
                o.CreatedAtUtc,
                firstLi?.Provider ?? "",
                firstLi?.FuelTypeId ?? "",
                totalLiters,
                totalQuantity,
                o.MonobankStatus?.ToString(),
                o.MonobankInvoiceId
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

        long GetProfit(Order o)
        {
            return (long)o.LineItems.Sum(li =>
            {
                if (fpLookup.TryGetValue((li.Provider, li.FuelTypeId, li.Liters), out var fp))
                    return (long)((fp.MarginUahPerLiter ?? 0) * (decimal)li.Liters * li.Quantity * 100m);
                return 0;
            });
        }

        var summary = new ReportSummary(
            orders.Sum(GetProfit),
            orders.Count,
            orders.Sum(o => o.LineItems.Sum(li => li.Quantity)),
            usedVouchers.Count,
            orders.Sum(o => o.LineItems.Sum(li => li.Liters * li.Quantity)),
            usedVouchers.Sum(v => v.Liters)
        );

        var monthlyGroups = orders
            .GroupBy(o => o.CreatedAtUtc.ToString("yyyy-MM"))
            .Select(g => new MonthlyBreakdown(
                g.Key,
                g.Sum(GetProfit),
                g.Sum(o => o.LineItems.Sum(li => li.Quantity)),
                0,
                g.Sum(o => o.LineItems.Sum(li => li.Liters * li.Quantity)),
                0
            ))
            .ToDictionary(b => b.Month);

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
                    v.Liters
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
