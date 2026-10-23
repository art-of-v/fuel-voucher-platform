using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.Redemptions;

/// <summary>
/// The voucher side of a redemption: report it, confirm or reject it, and reverse a confirmation.
/// </summary>
/// <remarks>
/// <para>
/// These methods assume the caller has opened a transaction. The handlers do that through
/// <c>ExecuteReplaySafeAsync</c>, so the status change, the claim, the audit event and the journal
/// drafts commit together. A statement import may call <see cref="ReportAsync"/> many times inside
/// one transaction, which is why the transaction does not live here.
/// </para>
/// <para>
/// The status transition is still a compare-and-swap: the status predicate is repeated in the
/// UPDATE, so two callers cannot both move the same voucher.
/// </para>
/// </remarks>
public sealed partial class VoucherRedemptionService
{
    private readonly ApplicationDbContext _context;
    private readonly RedemptionJournalPoster _journalPoster;
    private readonly JournalPostingService _journal;
    private readonly ILogger<VoucherRedemptionService> _logger;

    public VoucherRedemptionService(
        ApplicationDbContext context,
        RedemptionJournalPoster journalPoster,
        JournalPostingService journal,
        ILogger<VoucherRedemptionService> logger)
    {
        _context = context;
        _journalPoster = journalPoster;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>
    /// Records a claim and moves an assigned voucher to pending reconciliation.
    /// </summary>
    /// <param name="reportedByUserId">
    /// The customer for a self-report; NULL when the claim came from a supplier statement.
    /// </param>
    /// <param name="actorUserId">Who performs the write, for the audit event and journal drafts.</param>
    /// <param name="reportedLiters">The claimed litres; the voucher's own litres when NULL.</param>
    /// <param name="redeemedAtUtc">When the fuel was taken, as far as the evidence says.</param>
    /// <param name="supplierReference">The statement line reference, when there is one.</param>
    /// <param name="reason">Why the claim exists, shown on the voucher's lifecycle.</param>
    public async Task<RedemptionReportResult> ReportAsync(
        Guid voucherId,
        Guid? reportedByUserId,
        Guid actorUserId,
        decimal? reportedLiters,
        DateTime redeemedAtUtc,
        string? supplierReference,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var current = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Id == voucherId)
            .Select(v => new { v.Status, v.Liters })
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            return new RedemptionReportResult(RedemptionReportOutcome.NotFound, null, null);
        }

        if (current.Status == VoucherStatus.PendingReconciliation)
        {
            return new RedemptionReportResult(
                RedemptionReportOutcome.AlreadyPending,
                current.Status,
                await FindActiveRedemptionIdAsync(voucherId, cancellationToken));
        }

        if (current.Status == VoucherStatus.Used)
        {
            return new RedemptionReportResult(
                RedemptionReportOutcome.AlreadyUsed,
                current.Status,
                await FindConfirmedRedemptionIdAsync(voucherId, cancellationToken));
        }

        if (current.Status != VoucherStatus.Assigned)
        {
            return new RedemptionReportResult(RedemptionReportOutcome.InvalidState, current.Status, null);
        }

        var now = SystemClock.UtcNow;
        var liters = reportedLiters ?? current.Liters;
        if (liters <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reportedLiters),
                "A redemption claim must be for a positive number of litres.");
        }

        var affected = await _context.FuelVouchers
            .Where(v => v.Id == voucherId && v.Status == VoucherStatus.Assigned)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.PendingReconciliation)
                    // UsedAtUtc is deliberately NOT set here. The voucher has been reported, not
                    // confirmed; the redemption time belongs on the voucher only when the evidence
                    // is accepted.
                    .SetProperty(v => v.UpdatedAtUtc, now),
                cancellationToken);

        if (affected == 0)
        {
            var reread = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.Id == voucherId)
                .Select(v => (VoucherStatus?)v.Status)
                .FirstOrDefaultAsync(cancellationToken);

            return reread switch
            {
                null => new RedemptionReportResult(RedemptionReportOutcome.NotFound, null, null),
                VoucherStatus.PendingReconciliation => new RedemptionReportResult(
                    RedemptionReportOutcome.AlreadyPending,
                    reread,
                    await FindActiveRedemptionIdAsync(voucherId, cancellationToken)),
                VoucherStatus.Used => new RedemptionReportResult(
                    RedemptionReportOutcome.AlreadyUsed,
                    reread,
                    await FindConfirmedRedemptionIdAsync(voucherId, cancellationToken)),
                _ => new RedemptionReportResult(RedemptionReportOutcome.InvalidState, reread, null),
            };
        }

        var redemptionId = Guid.CreateVersion7();
        _context.VoucherRedemptions.Add(new VoucherRedemption
        {
            Id = redemptionId,
            VoucherId = voucherId,
            Status = VoucherRedemptionStatus.Reported,
            ReportedLiters = liters,
            RedeemedAtUtc = redeemedAtUtc,
            ReportedAtUtc = now,
            ReportedByUserId = reportedByUserId,
            SupplierReference = TrimReference(supplierReference),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.Assigned),
            ToStatus = nameof(VoucherStatus.PendingReconciliation),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = actorUserId,
            ExternalRef = TrimReference(supplierReference),
            Reason = TrimNote(reason),
            OccurredAtUtc = now,
        });

        await _context.SaveChangesAsync(cancellationToken);

        // Inside the caller's transaction: a status that says "reported" with no draft behind it
        // would be a sale nobody can confirm later.
        await _journalPoster.PostProvisionalAsync(voucherId, liters, actorUserId, redeemedAtUtc, cancellationToken);

        _logger.LogInformation(
            "Voucher {VoucherId} reported as used; claim {RedemptionId} is pending reconciliation.",
            voucherId, redemptionId);

        return new RedemptionReportResult(
            RedemptionReportOutcome.Reported,
            VoucherStatus.PendingReconciliation,
            redemptionId);
    }

    public async Task<RedemptionDecisionResult> ConfirmAsync(
        Guid voucherId,
        Guid operatorUserId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var current = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Id == voucherId)
            .Select(v => (VoucherStatus?)v.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            return new RedemptionDecisionResult(RedemptionDecisionOutcome.NotFound, null);
        }

        if (current == VoucherStatus.Used)
        {
            var confirmed = await FindConfirmedRedemptionIdAsync(voucherId, cancellationToken);
            return new RedemptionDecisionResult(
                confirmed is null ? RedemptionDecisionOutcome.InvalidState : RedemptionDecisionOutcome.AlreadyConfirmed,
                current);
        }

        if (current != VoucherStatus.PendingReconciliation)
        {
            return new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, current);
        }

        var redemption = await _context.VoucherRedemptions
            .FirstOrDefaultAsync(
                r => r.VoucherId == voucherId && r.Status == VoucherRedemptionStatus.Reported,
                cancellationToken);

        if (redemption is null)
        {
            return new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, current);
        }

        var now = SystemClock.UtcNow;
        var affected = await _context.FuelVouchers
            .Where(v => v.Id == voucherId && v.Status == VoucherStatus.PendingReconciliation)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.Used)
                    // The moment the fuel was taken, not the moment the paperwork caught up.
                    .SetProperty(v => v.UsedAtUtc, redemption.RedeemedAtUtc)
                    .SetProperty(v => v.UpdatedAtUtc, now),
                cancellationToken);

        if (affected == 0)
        {
            var reread = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.Id == voucherId)
                .Select(v => (VoucherStatus?)v.Status)
                .FirstOrDefaultAsync(cancellationToken);

            return reread switch
            {
                null => new RedemptionDecisionResult(RedemptionDecisionOutcome.NotFound, null),
                VoucherStatus.Used => new RedemptionDecisionResult(
                    RedemptionDecisionOutcome.AlreadyConfirmed, reread),
                _ => new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, reread),
            };
        }

        var resolutionNote = TrimNote(note);
        redemption.Status = VoucherRedemptionStatus.Confirmed;
        redemption.ConfirmedAtUtc = now;
        redemption.ConfirmedByUserId = operatorUserId;
        redemption.ResolutionNote = resolutionNote;
        redemption.UpdatedAtUtc = now;

        await ResolveStatementRowsAsync(
            redemption.Id,
            [RedemptionStatementStatus.Matched, RedemptionStatementStatus.Mismatch],
            RedemptionStatementStatus.Confirmed,
            operatorUserId,
            now,
            resolutionNote,
            cancellationToken);

        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.PendingReconciliation),
            ToStatus = nameof(VoucherStatus.Used),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = operatorUserId,
            Reason = resolutionNote,
            OccurredAtUtc = now,
        });

        await _context.SaveChangesAsync(cancellationToken);
        await _journal.ConfirmProvisionalAsync(voucherId, operatorUserId, cancellationToken);

        _logger.LogInformation(
            "Voucher {VoucherId} redemption {RedemptionId} confirmed by {OperatorUserId}.",
            voucherId, redemption.Id, operatorUserId);

        return new RedemptionDecisionResult(RedemptionDecisionOutcome.Confirmed, VoucherStatus.Used);
    }

    public async Task<RedemptionDecisionResult> RejectAsync(
        Guid voucherId,
        Guid operatorUserId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var current = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Id == voucherId)
            .Select(v => (VoucherStatus?)v.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            return new RedemptionDecisionResult(RedemptionDecisionOutcome.NotFound, null);
        }

        if (current == VoucherStatus.Assigned)
        {
            var latest = await FindLatestRedemptionAsync(voucherId, tracking: false, cancellationToken);
            return new RedemptionDecisionResult(
                latest?.Status == VoucherRedemptionStatus.Rejected
                    ? RedemptionDecisionOutcome.AlreadyRejected
                    : RedemptionDecisionOutcome.InvalidState,
                current);
        }

        if (current != VoucherStatus.PendingReconciliation)
        {
            return new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, current);
        }

        var redemption = await _context.VoucherRedemptions
            .FirstOrDefaultAsync(
                r => r.VoucherId == voucherId && r.Status == VoucherRedemptionStatus.Reported,
                cancellationToken);

        if (redemption is null)
        {
            return new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, current);
        }

        var now = SystemClock.UtcNow;
        var affected = await _context.FuelVouchers
            .Where(v => v.Id == voucherId && v.Status == VoucherStatus.PendingReconciliation)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.Assigned)
                    .SetProperty(v => v.UsedAtUtc, (DateTime?)null)
                    .SetProperty(v => v.UpdatedAtUtc, now),
                cancellationToken);

        if (affected == 0)
        {
            var reread = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.Id == voucherId)
                .Select(v => (VoucherStatus?)v.Status)
                .FirstOrDefaultAsync(cancellationToken);

            return reread switch
            {
                null => new RedemptionDecisionResult(RedemptionDecisionOutcome.NotFound, null),
                VoucherStatus.Assigned => new RedemptionDecisionResult(
                    RedemptionDecisionOutcome.AlreadyRejected, reread),
                _ => new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, reread),
            };
        }

        var resolutionNote = TrimNote(note);
        redemption.Status = VoucherRedemptionStatus.Rejected;
        redemption.ClosedAtUtc = now;
        redemption.ClosedByUserId = operatorUserId;
        redemption.ResolutionNote = resolutionNote;
        redemption.UpdatedAtUtc = now;

        await ResolveStatementRowsAsync(
            redemption.Id,
            [RedemptionStatementStatus.Matched, RedemptionStatementStatus.Mismatch],
            RedemptionStatementStatus.Rejected,
            operatorUserId,
            now,
            resolutionNote,
            cancellationToken);

        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.PendingReconciliation),
            ToStatus = nameof(VoucherStatus.Assigned),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = operatorUserId,
            Reason = resolutionNote,
            OccurredAtUtc = now,
        });

        await _context.SaveChangesAsync(cancellationToken);
        await _journal.DiscardProvisionalAsync(voucherId, cancellationToken);

        _logger.LogInformation(
            "Voucher {VoucherId} redemption {RedemptionId} rejected by {OperatorUserId}; the drafts were discarded.",
            voucherId, redemption.Id, operatorUserId);

        return new RedemptionDecisionResult(RedemptionDecisionOutcome.Rejected, VoucherStatus.Assigned);
    }

    private async Task<Guid?> FindActiveRedemptionIdAsync(Guid voucherId, CancellationToken cancellationToken)
        => await _context.VoucherRedemptions
            .AsNoTracking()
            .Where(r => r.VoucherId == voucherId && r.Status == VoucherRedemptionStatus.Reported)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Guid?> FindConfirmedRedemptionIdAsync(Guid voucherId, CancellationToken cancellationToken)
        => await _context.VoucherRedemptions
            .AsNoTracking()
            .Where(r => r.VoucherId == voucherId && r.Status == VoucherRedemptionStatus.Confirmed)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<VoucherRedemption?> FindLatestRedemptionAsync(
        Guid voucherId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = tracking
            ? _context.VoucherRedemptions
            : _context.VoucherRedemptions.AsNoTracking();

        return await query
            .Where(r => r.VoucherId == voucherId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ResolveStatementRowsAsync(
        Guid redemptionId,
        RedemptionStatementStatus[] from,
        RedemptionStatementStatus to,
        Guid operatorUserId,
        DateTime now,
        string? note,
        CancellationToken cancellationToken)
    {
        var rows = await _context.RedemptionStatementRows
            .Where(r => r.VoucherRedemptionId == redemptionId && from.Contains(r.Status))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.Status = to;
            row.ResolvedAtUtc = now;
            row.ResolvedByUserId = operatorUserId;
            row.ResolutionNote = note;
        }
    }

    private static string? TrimReference(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: > 100 } trimmed
            ? trimmed[..100]
            : value.Trim();

    private static string? TrimNote(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: > 500 } trimmed
            ? trimmed[..500]
            : value.Trim();
}

public enum RedemptionReportOutcome
{
    Reported,
    AlreadyPending,
    AlreadyUsed,
    InvalidState,
    NotFound,
}

public sealed record RedemptionReportResult(
    RedemptionReportOutcome Outcome,
    VoucherStatus? CurrentStatus,
    Guid? RedemptionId);

public enum RedemptionDecisionOutcome
{
    Confirmed,
    AlreadyConfirmed,
    Rejected,
    AlreadyRejected,
    Withdrawn,
    AlreadyWithdrawn,
    Reversed,
    AlreadyReversed,
    InvalidState,
    NotFound,
}

public sealed record RedemptionDecisionResult(
    RedemptionDecisionOutcome Outcome,
    VoucherStatus? CurrentStatus);
