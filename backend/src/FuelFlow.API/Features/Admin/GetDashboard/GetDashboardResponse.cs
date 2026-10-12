namespace FuelFlow.Features.Admin.GetDashboard;

public sealed record GetDashboardResponse(
    GetDashboardResponse.UsersStats Users,
    GetDashboardResponse.VouchersStats Vouchers,
    GetDashboardResponse.OrdersStats Orders)
{
    public sealed record UsersStats(int Total);

    public sealed record VouchersStats(
        int Total,
        int Available,
        int Assigned,
        int Used,
        int VerificationFailed,
        int VerifiedWithWarnings,
        IReadOnlyList<VouchersByProvider> ByProvider);

    public sealed record VouchersByProvider(string Provider, int Count);

    public sealed record OrdersStats(
        int Total,
        int Pending,
        int Fulfilled,
        long RevenueUah);
}
