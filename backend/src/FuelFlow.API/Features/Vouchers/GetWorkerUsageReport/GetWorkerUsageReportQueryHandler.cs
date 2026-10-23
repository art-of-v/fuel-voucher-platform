using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetWorkerUsageReport;

/// <summary>
/// What a worker was given, what they used, and what is left.
/// </summary>
/// <remarks>
/// The totals are computed over EVERY voucher the worker holds, not over the page. They used to
/// be computed from the rows the endpoint returned, which was the same number only because the
/// endpoint returned every row. A total that described the page would be a different number on
/// every page, and the summary is the part the worker reads.
/// </remarks>
public sealed class GetWorkerUsageReportQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetWorkerUsageReportQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<WorkerUsageReportPage> HandleAsync(
        GetWorkerUsageReportQuery query,
        CancellationToken cancellationToken = default)
    {
        // The same predicate the wallet already uses to decide what a worker holds
        // (GetUserVouchersCommandHandler): issued to this worker, for this company.
        var held = _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.WorkerUserId == query.UserId && v.LegalEntityId == query.LegalEntityId)
            .Where(v => !v.IsDeleted && v.Status != VoucherStatus.DecodeIntegrityFailed);

        // The count is asked of the same query the page is, so the two cannot disagree.
        var totalCount = await held.CountAsync(cancellationToken);

        var totals = await held
            .GroupBy(_ => 1)
            .Select(g => new WorkerUsageTotals(
                Count: g.Count(),
                LitersReceived: g.Sum(v => v.Liters),
                LitersUsed: g.Where(v => v.Status == VoucherStatus.Used).Sum(v => v.Liters),
                LitersPending: g.Where(v => v.Status == VoucherStatus.PendingReconciliation).Sum(v => v.Liters),
                LitersRemaining: g.Where(v => v.Status != VoucherStatus.Used
                    && v.Status != VoucherStatus.PendingReconciliation).Sum(v => v.Liters),
                CountUsed: g.Count(v => v.Status == VoucherStatus.Used),
                CountPending: g.Count(v => v.Status == VoucherStatus.PendingReconciliation),
                CountRemaining: g.Count(v => v.Status != VoucherStatus.Used
                    && v.Status != VoucherStatus.PendingReconciliation)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new WorkerUsageTotals(0, 0, 0, 0, 0, 0, 0, 0);

        var rows = await held
            .OrderByDescending(v => v.IssuedAtUtc)
            .ThenByDescending(v => v.CreatedAtUtc)
            // The tie-break: two vouchers can share an issue timestamp, and a page boundary
            // that falls between them would skip one. A skipped row is fuel the worker does
            // not know they hold.
            .ThenBy(v => v.Id)
            .Skip(query.Paging.Skip)
            .Take(query.Paging.PageSize)
            .Select(v => new WorkerUsageListItem(
                v.Id,
                v.Brand,
                v.FuelTypeId,
                v.VoucherNumber,
                v.Liters,
                v.Status.ToString(),
                v.CustomerExpirationDate,
                v.IssuedAtUtc,
                v.UsedAtUtc,
                v.Status == VoucherStatus.Used))
            .ToListAsync(cancellationToken);

        return new WorkerUsageReportPage(totalCount, totals, rows);
    }
}

/// <summary>
/// One page of the report, with the count of everything that matched.
/// </summary>
/// <param name="TotalCount">How many vouchers the worker holds for the company, across every
/// page.</param>
/// <param name="Totals">The summary over every voucher the worker holds.</param>
/// <param name="Items">The page itself.</param>
public sealed record WorkerUsageReportPage(
    int TotalCount,
    WorkerUsageTotals Totals,
    List<WorkerUsageListItem> Items);
