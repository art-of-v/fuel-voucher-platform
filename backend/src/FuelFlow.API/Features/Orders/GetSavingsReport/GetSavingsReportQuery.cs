namespace FuelFlow.Features.Orders.GetSavingsReport;

/// <summary>Customer-scoped savings summary for the signed-in user.</summary>
public sealed record GetSavingsReportQuery(Guid UserId);
