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

    public sealed record VouchersByProvider(string Brand, int Count);

    public sealed record OrdersStats(
        int Total,
        int Pending,
        int Fulfilled,
        /// <summary>
        /// The platform's margin in KOPIECKS. The name says Uah and the value has always been
        /// kopecks - nothing consumes this endpoint yet, so the unit is written down here instead
        /// of being changed under a caller that would then read a hundred times too little.
        /// </summary>
        long RevenueUah);
}
