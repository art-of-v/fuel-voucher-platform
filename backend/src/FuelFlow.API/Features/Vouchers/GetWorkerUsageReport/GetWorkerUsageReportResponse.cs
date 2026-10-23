namespace FuelFlow.Features.Vouchers.GetWorkerUsageReport;

/// <summary>One voucher in the worker's report.</summary>
/// <param name="IssuedAtUtc">When the owner handed it over; null for a voucher issued before this
/// feature existed, since the column is only populated from now on.</param>
/// <param name="UsedAtUtc">When it was refuelled with; null while unused.</param>
public sealed record WorkerUsageListItem(
    Guid Id,
    string Brand,
    Guid FuelTypeId,
    string VoucherNumber,
    decimal Liters,
    string Status,
    DateOnly ExpirationDate,
    DateTime? IssuedAtUtc,
    DateTime? UsedAtUtc,
    bool IsUsed);

public sealed record WorkerUsageTotals(
    int Count,
    decimal LitersReceived,
    decimal LitersUsed,
    decimal LitersPending,
    decimal LitersRemaining,
    int CountUsed,
    int CountPending,
    int CountRemaining);

public sealed record GetWorkerUsageReportResponse(
    Guid LegalEntityId,
    WorkerUsageTotals Totals,
    List<WorkerUsageListItem> Items);
