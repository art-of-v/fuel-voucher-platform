using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Redemptions.DismissStatementRow;

public sealed record DismissRedemptionStatementRowCommand(
    Guid RowId,
    Guid UserId,
    string? Note);

public sealed record DismissRedemptionStatementRowResponse(
    bool Success,
    string Message,
    string? ErrorCode = null);

public sealed class DismissRedemptionStatementRowCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DismissRedemptionStatementRowCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DismissRedemptionStatementRowResponse> HandleAsync(
        DismissRedemptionStatementRowCommand command,
        CancellationToken cancellationToken = default)
    {
        var row = await _context.RedemptionStatementRows
            .AsNoTracking()
            .Where(r => r.Id == command.RowId)
            .Select(r => new { r.Status, r.VoucherRedemptionId })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new DismissRedemptionStatementRowResponse(
                false, "Statement line not found", RedemptionRefusalCodes.StatementLineNotFound);
        }

        if (row.Status == RedemptionStatementStatus.Dismissed)
        {
            return new DismissRedemptionStatementRowResponse(true, "Statement line was already dismissed");
        }

        if (!CanDismiss(row.Status))
        {
            return new DismissRedemptionStatementRowResponse(
                false,
                $"Statement line is {row.Status}; resolve the voucher instead.",
                RedemptionRefusalCodes.StatementLineNotDismissable);
        }

        if (row.VoucherRedemptionId is not null
            && await _context.VoucherRedemptions
                .AsNoTracking()
                .AnyAsync(
                    r => r.Id == row.VoucherRedemptionId.Value
                        && r.Status == VoucherRedemptionStatus.Reported,
                    cancellationToken))
        {
            return new DismissRedemptionStatementRowResponse(
                false,
                "The line belongs to a pending claim; confirm or reject the claim first.",
                RedemptionRefusalCodes.StatementLineHasPendingClaim);
        }

        var now = SystemClock.UtcNow;
        var note = Truncate(command.Note, 500) ?? "Dismissed by the operator";

        var affected = await _context.RedemptionStatementRows
            .Where(r => r.Id == command.RowId && r.Status == row.Status)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, RedemptionStatementStatus.Dismissed)
                    .SetProperty(r => r.ResolvedAtUtc, now)
                    .SetProperty(r => r.ResolvedByUserId, command.UserId)
                    .SetProperty(r => r.ResolutionNote, note),
                cancellationToken);

        if (affected == 1)
        {
            return new DismissRedemptionStatementRowResponse(true, "Statement line dismissed");
        }

        var current = await _context.RedemptionStatementRows
            .AsNoTracking()
            .Where(r => r.Id == command.RowId)
            .Select(r => (RedemptionStatementStatus?)r.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return current switch
        {
            null => new DismissRedemptionStatementRowResponse(
                false, "Statement line not found", RedemptionRefusalCodes.StatementLineNotFound),
            RedemptionStatementStatus.Dismissed => new DismissRedemptionStatementRowResponse(
                true, "Statement line was already dismissed"),
            _ => new DismissRedemptionStatementRowResponse(
                false,
                "Statement line changed before the dismiss landed.",
                RedemptionRefusalCodes.StatementLineChanged),
        };
    }

    private static bool CanDismiss(RedemptionStatementStatus status) => status is
        RedemptionStatementStatus.Matched
        or RedemptionStatementStatus.Mismatch
        or RedemptionStatementStatus.UnknownVoucher
        or RedemptionStatementStatus.AlreadyConfirmed
        or RedemptionStatementStatus.NotAssigned;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
