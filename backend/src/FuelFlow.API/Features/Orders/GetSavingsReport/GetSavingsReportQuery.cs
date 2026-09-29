namespace FuelFlow.Features.Orders.GetSavingsReport;

/// <summary>
/// Customer-scoped savings summary for the signed-in user, optionally narrowed to a period.
/// <paramref name="FromDate"/>/<paramref name="ToDate"/> filter orders by <c>CreatedAtUtc</c>; both
/// null means "all time". The remaining-balance figures are always a current snapshot and ignore
/// the period (you still own those litres today regardless of when you bought them).
/// </summary>
public sealed record GetSavingsReportQuery(Guid UserId, DateTime? FromDate = null, DateTime? ToDate = null);
