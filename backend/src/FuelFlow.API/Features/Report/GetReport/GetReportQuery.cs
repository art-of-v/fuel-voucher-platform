namespace FuelFlow.Features.Report.GetReport;

public sealed record GetReportQuery(
    Guid? UserId,
    DateTime? FromDate = null,
    DateTime? ToDate = null
);
