namespace FuelFlow.Features.Admin.GetReconciliation;

public sealed record GetReconciliationResponse(
    GetReconciliationResponse.SummaryData Summary,
    IReadOnlyList<GetReconciliationResponse.ThreeWayMatchItem> ThreeWayMatch,
    IReadOnlyList<GetReconciliationResponse.ExceptionItem> Exceptions,
    IReadOnlyList<GetReconciliationResponse.VoucherFunnelItem> VoucherFunnel,
    IReadOnlyList<GetReconciliationResponse.RevenueSummaryItem> RevenueSummary)
{
    /// <summary>
    /// The counters the screen puts at the top.
    /// </summary>
    /// <remarks>
    /// <b>The units are not all the same, and the names say which is which.</b> The revenue is
    /// in UAH - the margin is UAH per litre, so margin times litres is UAH - while the three
    /// money figures under it are in kopecks, which is what the money path stores and what the
    /// brand's API takes. The field used to say <c>TotalRevenueKopecks</c> and hold UAH, which
    /// is a number that lies about itself in a summary of money; the screen was right (it
    /// shows the revenue as UAH without dividing) and the name was wrong. Renamed rather than
    /// converted, because a conversion would move the number under every reader of it.
    /// </remarks>
    public sealed record SummaryData(
        int TotalOrders,
        int PaidUnfulfilled,
        int PartiallyFulfilled,
        int Fulfilled,
        decimal TotalRevenueUah,
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
        /// <summary>The revenue of the month, in UAH - see the summary for the units.</summary>
        decimal RevenueUah);
}
