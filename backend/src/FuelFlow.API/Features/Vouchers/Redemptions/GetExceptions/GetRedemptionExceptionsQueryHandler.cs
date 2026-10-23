using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Redemptions.GetExceptions;

public sealed record GetRedemptionExceptionsQuery;

public sealed record PendingRedemptionDetail(
    Guid RedemptionId,
    Guid VoucherId,
    string VoucherNumber,
    Guid? ReportedByUserId,
    decimal ReportedLiters,
    DateTime RedeemedAtUtc,
    DateTime ReportedAtUtc,
    string? SupplierReference,
    bool HasMismatch,
    string Severity,
    IReadOnlyList<StatementLineDetail> StatementLines);

public sealed record StatementLineDetail(
    Guid RedemptionId,
    Guid RowId,
    string ExternalId,
    decimal Liters,
    DateTime? RedeemedAtUtc,
    RedemptionStatementStatus Status,
    string? Note);

public sealed record StatementExceptionDetail(
    Guid RowId,
    string ExternalId,
    string VoucherNumber,
    decimal Liters,
    DateTime? RedeemedAtUtc,
    RedemptionStatementStatus Status,
    Guid? VoucherId,
    Guid? RedemptionId,
    DateTime ImportedAtUtc,
    string? Note,
    string Severity);

public sealed record GetRedemptionExceptionsResponse(
    int PendingClaims,
    int MismatchedClaims,
    int StatementExceptions,
    IReadOnlyList<PendingRedemptionDetail> Pending,
    IReadOnlyList<StatementExceptionDetail> Statements);

public sealed class GetRedemptionExceptionsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetRedemptionExceptionsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetRedemptionExceptionsResponse> HandleAsync(
        GetRedemptionExceptionsQuery query,
        CancellationToken cancellationToken = default)
    {
        _ = query;

        var pending = await (
            from r in _context.VoucherRedemptions.AsNoTracking()
            join v in _context.FuelVouchers on r.VoucherId equals v.Id
            where r.Status == VoucherRedemptionStatus.Reported
            orderby r.ReportedAtUtc
            select new
            {
                r.Id,
                r.VoucherId,
                v.VoucherNumber,
                r.ReportedByUserId,
                r.ReportedLiters,
                r.RedeemedAtUtc,
                r.ReportedAtUtc,
                r.SupplierReference,
            }).ToListAsync(cancellationToken);

        var pendingIds = pending.Select(p => p.Id).ToArray();
        var statementLines = pendingIds.Length == 0
            ? new List<StatementLineDetail>()
            : await _context.RedemptionStatementRows
                .AsNoTracking()
                .Where(r => r.VoucherRedemptionId != null
                    && pendingIds.Contains(r.VoucherRedemptionId.Value)
                    && (r.Status == RedemptionStatementStatus.Matched
                        || r.Status == RedemptionStatementStatus.Mismatch))
                .OrderBy(r => r.ImportedAtUtc)
                .Select(r => new StatementLineDetail(
                    r.VoucherRedemptionId!.Value,
                    r.Id,
                    r.ExternalId,
                    r.Liters,
                    r.RedeemedAtUtc,
                    r.Status,
                    r.Note))
                .ToListAsync(cancellationToken);

        var linesByRedemption = statementLines
            .GroupBy(l => l.RedemptionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<StatementLineDetail>)g.ToList());

        var pendingDetails = pending.Select(p =>
        {
            var lines = linesByRedemption.TryGetValue(p.Id, out var found)
                ? found
                : Array.Empty<StatementLineDetail>();
            var mismatch = lines.Any(l => l.Status == RedemptionStatementStatus.Mismatch);
            return new PendingRedemptionDetail(
                p.Id,
                p.VoucherId,
                p.VoucherNumber,
                p.ReportedByUserId,
                p.ReportedLiters,
                p.RedeemedAtUtc,
                p.ReportedAtUtc,
                p.SupplierReference,
                mismatch,
                mismatch ? "critical" : "warning",
                lines);
        }).ToList();

        var statementExceptions = await _context.RedemptionStatementRows
            .AsNoTracking()
            .Where(r =>
                r.Status == RedemptionStatementStatus.UnknownVoucher
                || r.Status == RedemptionStatementStatus.AlreadyConfirmed
                || r.Status == RedemptionStatementStatus.NotAssigned
                || ((r.Status == RedemptionStatementStatus.Matched
                        || r.Status == RedemptionStatementStatus.Mismatch)
                    && (r.VoucherRedemptionId == null
                        || !_context.VoucherRedemptions.Any(x =>
                            x.Id == r.VoucherRedemptionId
                            && x.Status == VoucherRedemptionStatus.Reported))))
            .OrderByDescending(r => r.ImportedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.ExternalId,
                r.VoucherNumber,
                r.Liters,
                r.RedeemedAtUtc,
                r.Status,
                r.VoucherId,
                r.VoucherRedemptionId,
                r.ImportedAtUtc,
                r.Note,
            })
            .ToListAsync(cancellationToken);

        var statementDetails = statementExceptions.Select(r => new StatementExceptionDetail(
            r.Id,
            r.ExternalId,
            r.VoucherNumber,
            r.Liters,
            r.RedeemedAtUtc,
            r.Status,
            r.VoucherId,
            r.VoucherRedemptionId,
            r.ImportedAtUtc,
            r.Note,
            SeverityOf(r.Status))).ToList();

        return new GetRedemptionExceptionsResponse(
            pendingDetails.Count,
            pendingDetails.Count(p => p.HasMismatch),
            statementDetails.Count,
            pendingDetails,
            statementDetails);
    }

    private static string SeverityOf(RedemptionStatementStatus status) => status switch
    {
        RedemptionStatementStatus.UnknownVoucher => "critical",
        RedemptionStatementStatus.Mismatch => "critical",
        _ => "warning",
    };
}
