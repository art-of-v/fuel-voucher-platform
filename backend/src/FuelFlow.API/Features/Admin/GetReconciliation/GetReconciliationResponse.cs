using FuelFlow.Persistence;

namespace FuelFlow.Features.Admin.GetReconciliation;

public sealed record GetReconciliationResponse(
    GetReconciliationResponse.SummaryData Summary,
    PagedResult<GetReconciliationResponse.ThreeWayMatchItem> ThreeWayMatch,
    IReadOnlyList<GetReconciliationResponse.ExceptionItem> Exceptions,
    IReadOnlyList<GetReconciliationResponse.VoucherFunnelItem> VoucherFunnel,
    IReadOnlyList<GetReconciliationResponse.RevenueSummaryItem> RevenueSummary)
{
    public sealed record SummaryData(
        int TotalOrders,
        int PaidUnfulfilled,
        int PartiallyFulfilled,
        int Fulfilled,
        decimal TotalRevenueKopecks,
        int OrphanVouchers,
        int UnprocessedEvents,
        int LowInventoryProviders,
        int ImportErrors7d,
        int RefundedOrders,
        decimal TotalReceivedKopecks,
        decimal TotalFulfilledValueKopecks,
        decimal TotalRefundedKopecks);

    public sealed record ThreeWayMatchItem(
        Guid OrderId,
        string? UserPhone,
        string Brand,
        Guid FuelType,
        decimal Liters,
        int Quantity,
        decimal TotalPrice,
        string OrderStatus,
        string? MonobankStatus,
        int VouchersExpected,
        int VouchersDelivered,
        string MatchStatus,
        int DaysSinceCreated,
        DateTime CreatedAtUtc,
        string? RefundStatus,
        decimal RefundedKopecks);

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
        decimal RevenueKopecks);
}
