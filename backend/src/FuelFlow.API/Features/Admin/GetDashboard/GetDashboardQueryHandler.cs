using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
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

        var totalOrders = await _context.Orders.CountAsync(cancellationToken);
        var pendingOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled, cancellationToken);
        var fulfilledOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Fulfilled, cancellationToken);

        var revenueUah = await _context.Orders
            .Where(o => o.Status == OrderStatus.Fulfilled || o.Status == OrderStatus.PartiallyFulfilled)
            .SumAsync(o => (long)o.Price, cancellationToken);

        var byProvider = await _context.FuelVouchers
            .GroupBy(v => v.Provider)
            .Select(g => new GetDashboardResponse.VouchersByProvider(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return new GetDashboardResponse(
            new GetDashboardResponse.UsersStats(totalUsers),
            new GetDashboardResponse.VouchersStats(totalVouchers, availableVouchers, assignedVouchers, usedVouchers, byProvider),
            new GetDashboardResponse.OrdersStats(totalOrders, pendingOrders, fulfilledOrders, revenueUah));
    }
}
