using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Admin.GetDashboard;

public sealed class GetDashboardQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetDashboardQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetDashboardResponse> HandleAsync(
        GetDashboardQuery query,
        CancellationToken cancellationToken = default)
    {
        var totalUsers = await _context.Users.CountAsync(cancellationToken);
        var totalVouchers = await _context.FuelVouchers.CountAsync(cancellationToken);
        var availableVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Available || v.Status == VoucherStatus.Imported, cancellationToken);
        var assignedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Assigned, cancellationToken);
        var usedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Used, cancellationToken);
        var verificationFailedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.VerificationFailed, cancellationToken);
        var verifiedWithWarningsVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.VerifiedWithWarnings, cancellationToken);

        var totalOrders = await _context.Orders.CountAsync(cancellationToken);
        var pendingOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled, cancellationToken);
        var fulfilledOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Fulfilled, cancellationToken);

        var fuelPackages = await _context.FuelPackages
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var fpLookup = fuelPackages
            .GroupBy(fp => (fp.StationId, fp.FuelTypeId, fp.Liters))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(fp => fp.PriceUpdatedAt ?? fp.CreatedAtUtc).First());

        var fulfilledOrdersList = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Where(o => o.Status == OrderStatus.Fulfilled || o.Status == OrderStatus.PartiallyFulfilled)
            .ToListAsync(cancellationToken);

        var profitKopecks = (long)fulfilledOrdersList.Sum(o =>
            o.LineItems.Sum(li =>
            {
                if (fpLookup.TryGetValue((li.Provider, li.FuelTypeId, li.Liters), out var fp))
                    return (long)((fp.MarginUahPerLiter ?? 0) * (decimal)li.Liters * li.Quantity * 100m);
                return 0;
            })
        );

        var revenueUah = profitKopecks;

        var byProvider = await _context.FuelVouchers
            .GroupBy(v => v.Provider)
            .Select(g => new GetDashboardResponse.VouchersByProvider(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return new GetDashboardResponse(
            new GetDashboardResponse.UsersStats(totalUsers),
            new GetDashboardResponse.VouchersStats(totalVouchers, availableVouchers, assignedVouchers, usedVouchers, verificationFailedVouchers, verifiedWithWarningsVouchers, byProvider),
            new GetDashboardResponse.OrdersStats(totalOrders, pendingOrders, fulfilledOrders, revenueUah));
    }
}
