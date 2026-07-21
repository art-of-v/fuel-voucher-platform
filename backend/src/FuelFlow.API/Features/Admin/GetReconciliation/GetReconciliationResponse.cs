namespace FuelFlow.Features.Admin.GetReconciliation;

public sealed record GetReconciliationResponse(
    GetReconciliationResponse.SummaryData Summary,
    IReadOnlyList<GetReconciliationResponse.PendingOrderItem> PendingOrders,
    IReadOnlyList<GetReconciliationResponse.VoucherInventoryItem> VoucherInventory,
    IReadOnlyList<GetReconciliationResponse.MonthlyRevenueItem> MonthlyRevenue,
    IReadOnlyList<GetReconciliationResponse.ImportSummaryItem> RecentImports)
{
    public sealed record SummaryData(
        int TotalOrders,
        int FulfilledOrders,
        int PendingOrders,
        int PartiallyFulfilledOrders,
        int CancelledOrders,
        long TotalRevenueKopecks,
        int TotalVouchers,
        int AvailableVouchers,
        int AssignedVouchers,
        int UsedVouchers,
        int FailedVouchers,
        int UnprocessedOutboxEvents,
        int RecentImportErrors);

    public sealed record PendingOrderItem(
        Guid OrderId,
        Guid UserId,
        string Provider,
        string FuelTypeId,
        decimal Liters,
        int Quantity,
        int Price,
        string Status,
        string? MonobankInvoiceId,
        string? MonobankStatus,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    public sealed record VoucherInventoryItem(
        string Provider,
        string FuelTypeId,
        string Status,
        int Count,
        decimal TotalLiters);

    public sealed record MonthlyRevenueItem(
        int Year,
        int Month,
        string Label,
        int OrderCount,
        long RevenueKopecks);

    public sealed record ImportSummaryItem(
        Guid ImportId,
        string FileName,
        string Status,
        int TotalVouchers,
        int ErrorCount,
        int WarningCount,
        int VerificationFailedCount,
        DateTime CreatedAtUtc);
}
