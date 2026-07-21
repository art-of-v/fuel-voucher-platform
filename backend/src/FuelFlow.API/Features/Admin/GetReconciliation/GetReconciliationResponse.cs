namespace FuelFlow.Features.Admin.GetReconciliation;

public sealed record GetReconciliationResponse(
    GetReconciliationResponse.SummaryData Summary,
    IReadOnlyList<GetReconciliationResponse.ThreeWayMatchItem> ThreeWayMatch,
    IReadOnlyList<GetReconciliationResponse.ExceptionItem> Exceptions,
    IReadOnlyList<GetReconciliationResponse.VoucherFunnelItem> VoucherFunnel,
    IReadOnlyList<GetReconciliationResponse.RevenueSummaryItem> RevenueSummary)
{
    public sealed record SummaryData(
        int TotalOrders,
        int PaidUnfulfilled,
        int PartiallyFulfilled,
        int Fulfilled,
        long TotalRevenueKopecks,
        int OrphanVouchers,
        int UnprocessedEvents,
        int LowInventoryProviders,
        int ImportErrors7d);

    public sealed record ThreeWayMatchItem(
        Guid OrderId,
        string? UserPhone,
        string Provider,
        string FuelType,
        decimal Liters,
        int Quantity,
        int TotalPrice,
        string OrderStatus,
        string? MonobankStatus,
        int VouchersExpected,
        int VouchersDelivered,
        string MatchStatus,
        int DaysSinceCreated,
        DateTime CreatedAtUtc);

    public sealed record ExceptionItem(
        Guid Id,
        string Type,
        string Description,
        string Severity,
        DateTime CreatedAtUtc);

    public sealed record VoucherFunnelItem(
        string Status,
        int Count,
        decimal TotalLiters);

    public sealed record RevenueSummaryItem(
        int Year,
        int Month,
        int OrderCount,
        long RevenueKopecks);
}
