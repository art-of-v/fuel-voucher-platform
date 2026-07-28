namespace FuelFlow.Features.Report.GetReport;

public sealed record ReportPeriod(
    DateTime? From,
    DateTime? To
);

public sealed record ReportSummary(
    long TotalSpent,
    int TotalOrders,
    int VouchersPurchased,
    int VouchersUsed,
    decimal TotalLitersPurchased,
    decimal TotalLitersUsed
);

public sealed record PaymentEntry(
    Guid OrderId,
    int Amount,
    string Status,
    DateTime CreatedAtUtc,
    string Provider,
    string FuelType,
    decimal Liters,
    int Quantity,
    string? MonobankStatus,
    string? MonobankInvoiceId
);

public sealed record RedemptionEntry(
    Guid VoucherId,
    string Provider,
    string FuelType,
    string FuelName,
    decimal Liters,
    DateTime RedeemedAt
);

public sealed record MonthlyBreakdown(
    string Month,
    long TotalSpent,
    int VouchersPurchased,
    int VouchersUsed,
    decimal TotalLitersPurchased,
    decimal TotalLitersUsed
);

public sealed record GetReportResponse(
    ReportPeriod Period,
    ReportSummary Summary,
    List<PaymentEntry> Payments,
    List<RedemptionEntry> Redemptions,
    List<MonthlyBreakdown> MonthlyBreakdown
);
