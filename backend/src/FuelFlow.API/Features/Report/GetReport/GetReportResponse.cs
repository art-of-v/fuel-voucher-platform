namespace FuelFlow.Features.Report.GetReport;

public sealed record ReportPeriod(
    DateTime? From,
    DateTime? To
);

public sealed record ReportSummary(
    decimal TotalSpent,
    int TotalOrders,
    int VouchersPurchased,
    int VouchersUsed,
    decimal TotalLitersPurchased,
    decimal TotalLitersUsed,
    decimal TotalReceivedKopecks,
    decimal TotalFulfilledValueKopecks,
    decimal TotalRefundedKopecks
);

public sealed record PaymentEntry(
    Guid OrderId,
    decimal Amount,
    string Status,
    DateTime CreatedAtUtc,
    string Brand,
    Guid FuelType,
    string FuelName,
    decimal Liters,
    int Quantity,
    string? MonobankStatus,
    string? MonobankInvoiceId,
    decimal FulfilledValueKopecks,
    decimal RefundedKopecks,
    string? RefundStatus
);

public sealed record RedemptionEntry(
    Guid VoucherId,
    string Brand,
    Guid FuelType,
    string FuelName,
    decimal Liters,
    DateTime RedeemedAt
);

public sealed record MonthlyBreakdown(
    string Month,
    decimal TotalSpent,
    int VouchersPurchased,
    int VouchersUsed,
    decimal TotalLitersPurchased,
    decimal TotalLitersUsed,
    decimal TotalRefundedKopecks
);

public sealed record GetReportResponse(
    ReportPeriod Period,
    ReportSummary Summary,
    List<PaymentEntry> Payments,
    List<RedemptionEntry> Redemptions,
    List<MonthlyBreakdown> MonthlyBreakdown
);
