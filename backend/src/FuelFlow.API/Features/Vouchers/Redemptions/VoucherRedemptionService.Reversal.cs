using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Redemptions;

public sealed partial class VoucherRedemptionService
{
    /// <summary>
    /// The holder's own undo, available only while the claim is still waiting for evidence.
    /// </summary>
    /// <remarks>
    /// A confirmed redemption is a posted sale; undoing it is an operator reversal, not a customer
    /// toggle. Withdrawing a pending claim discards the drafts and leaves any supplier line in the
    /// queue, because the statement still says the fuel was taken.
    /// </remarks>
    public async Task<RedemptionDecisionResult> WithdrawAsync(
        Guid voucherId,
        Guid customerUserId,
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
                latest?.Status == VoucherRedemptionStatus.Withdrawn
                    ? RedemptionDecisionOutcome.AlreadyWithdrawn
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
                    RedemptionDecisionOutcome.AlreadyWithdrawn, reread),
                _ => new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, reread),
            };
        }

        var resolutionNote = TrimNote(note);
        redemption.Status = VoucherRedemptionStatus.Withdrawn;
        redemption.ClosedAtUtc = now;
        redemption.ClosedByUserId = customerUserId;
        redemption.ResolutionNote = resolutionNote;
        redemption.UpdatedAtUtc = now;

        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.PendingReconciliation),
            ToStatus = nameof(VoucherStatus.Assigned),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = customerUserId,
            Reason = resolutionNote,
            OccurredAtUtc = now,
        });

        await _context.SaveChangesAsync(cancellationToken);
        await _journal.DiscardProvisionalAsync(voucherId, cancellationToken);

        _logger.LogInformation(
            "Voucher {VoucherId} redemption {RedemptionId} withdrawn by {CustomerUserId}; the drafts were discarded.",
            voucherId, redemption.Id, customerUserId);

        return new RedemptionDecisionResult(RedemptionDecisionOutcome.Withdrawn, VoucherStatus.Assigned);
    }

    public async Task<RedemptionDecisionResult> ReverseAsync(
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
                latest?.Status == VoucherRedemptionStatus.Reversed
                    ? RedemptionDecisionOutcome.AlreadyReversed
                    : RedemptionDecisionOutcome.InvalidState,
                current);
        }

        if (current != VoucherStatus.Used)
        {
            return new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, current);
        }

        var redemption = await _context.VoucherRedemptions
            .FirstOrDefaultAsync(
                r => r.VoucherId == voucherId && r.Status == VoucherRedemptionStatus.Confirmed,
                cancellationToken);

        if (redemption is null)
        {
            return new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, current);
        }

        var now = SystemClock.UtcNow;
        var affected = await _context.FuelVouchers
            .Where(v => v.Id == voucherId && v.Status == VoucherStatus.Used)
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
                    RedemptionDecisionOutcome.AlreadyReversed, reread),
                _ => new RedemptionDecisionResult(RedemptionDecisionOutcome.InvalidState, reread),
            };
        }

        var resolutionNote = TrimNote(note);
        redemption.Status = VoucherRedemptionStatus.Reversed;
        redemption.ClosedAtUtc = now;
        redemption.ClosedByUserId = operatorUserId;
        redemption.ResolutionNote = resolutionNote;
        redemption.UpdatedAtUtc = now;

        await ResolveStatementRowsAsync(
            redemption.Id,
            [RedemptionStatementStatus.Confirmed],
            RedemptionStatementStatus.Reversed,
            operatorUserId,
            now,
            resolutionNote,
            cancellationToken);

        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = voucherId,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.Used),
            ToStatus = nameof(VoucherStatus.Assigned),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = operatorUserId,
            Reason = resolutionNote,
            OccurredAtUtc = now,
        });

        await _context.SaveChangesAsync(cancellationToken);

        var entries = await _context.JournalEntries
            .Where(e => e.SourceId == voucherId
                && e.SourceType == JournalSourceType.Fulfillment
                && e.Status == JournalEntryStatus.Posted)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var reason = resolutionNote ?? "Redemption reversed by the operator";
        foreach (var entryId in entries)
        {
            await _journal.ReverseAsync(entryId, operatorUserId, reason, cancellationToken);
        }

        if (entries.Count == 0)
        {
            _logger.LogWarning(
                "Voucher {VoucherId} was reversed but no posted journal entry was found for it.",
                voucherId);
        }

        _logger.LogInformation(
            "Voucher {VoucherId} redemption {RedemptionId} reversed by {OperatorUserId}; {Entries} entries were reversed.",
            voucherId, redemption.Id, operatorUserId, entries.Count);

        return new RedemptionDecisionResult(RedemptionDecisionOutcome.Reversed, VoucherStatus.Assigned);
    }
}
