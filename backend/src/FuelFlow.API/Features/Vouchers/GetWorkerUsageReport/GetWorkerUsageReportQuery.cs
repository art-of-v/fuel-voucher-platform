using FuelFlow.Persistence;

namespace FuelFlow.Features.Vouchers.GetWorkerUsageReport;

/// <summary>
/// One page of the fuel a worker holds for one company.
/// </summary>
/// <param name="UserId">The worker. A worker sees only their own vouchers.</param>
/// <param name="LegalEntityId">The company the vouchers were issued by.</param>
/// <param name="Paging">Which page, and how big it is. The page size is clamped by
/// <see cref="PagedRequest"/>, so a client asking for "?pageSize=2000000000" gets the maximum
/// and not the whole table.</param>
public sealed record GetWorkerUsageReportQuery(Guid UserId, Guid LegalEntityId, PagedRequest Paging);
