using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.UnblockWorkerVoucher;

public sealed record UnblockWorkerVoucherCommand(Guid OwnerUserId, Guid VoucherId, Guid? LegalEntityId = null);

public sealed record UnblockWorkerVoucherResult(string Status, string? ErrorMessage = null);

/// <summary>
/// Owner-side thaw of a voucher the owner previously froze with
/// <see cref="BlockWorkerVoucher"/> (#103 S3b): flips a <c>Blocked</c> voucher back to
/// <c>Assigned</c>, leaving it with the same worker so it becomes redeemable again. The
/// one toggle counterpart to block; recalling to the pool stays a separate action.
/// </summary>
public sealed class UnblockWorkerVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public UnblockWorkerVoucherCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<UnblockWorkerVoucherResult> HandleAsync(UnblockWorkerVoucherCommand command, CancellationToken cancellationToken = default)
    {
        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new UnblockWorkerVoucherResult("CompanyNotOwned", "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new UnblockWorkerVoucherResult("OwnerCompanyNotFound", "Owner does not have a legal entity profile.");
        }

        // Read-only guard, so AsNoTracking (#246). The thaw itself is a compare-and-swap
        // ExecuteUpdateAsync further down, which bumps xmin behind EF's back; a tracked instance of
        // that same voucher would sit in the change tracker holding the pre-statement xmin, and the
        // first thing that later modified it would fail on a conflict that never happened.
        var voucher = await _context.FuelVouchers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == command.VoucherId, cancellationToken);

        if (voucher is null)
        {
            return new UnblockWorkerVoucherResult("NotFound", "Voucher not found.");
        }

        if (voucher.LegalEntityId != legalEntityId || voucher.AssignedToUserId != command.OwnerUserId)
        {
            return new UnblockWorkerVoucherResult("Forbidden", "Voucher does not belong to this company owner.");
        }

        if (voucher.Status != VoucherStatus.Blocked || voucher.WorkerUserId is null)
        {
            return new UnblockWorkerVoucherResult("InvalidState", "Only a worker's blocked voucher can be unblocked.");
        }

        // The thaw and the record of it commit together - see the same note in
        // BlockWorkerVoucherCommand. Without it a failed SaveChanges leaves an Assigned voucher with
        // nothing saying who released it.
        await using var tx = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var unblockedAtUtc = DateTime.UtcNow;
        var affected = await _context.FuelVouchers
            .Where(v => v.Id == command.VoucherId
                        && v.Status == VoucherStatus.Blocked
                        && v.WorkerUserId != null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.Assigned)
                    .SetProperty(v => v.UpdatedAtUtc, unblockedAtUtc),
                cancellationToken);

        if (affected == 0)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(cancellationToken);
            }

            return new UnblockWorkerVoucherResult("InvalidState", "Only a worker's blocked voucher can be unblocked.");
        }

        // D-13. Event only, exactly like the block: an unblock does not move the paper either. The
        // worker never lost custody, so there is nothing for a movement to say.
        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.CreateVersion7(),
            VoucherId = command.VoucherId,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.Blocked),
            ToStatus = nameof(VoucherStatus.Assigned),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = command.OwnerUserId,
            ActorPartyId = legalEntityId,
            OccurredAtUtc = unblockedAtUtc
        });

        await _context.SaveChangesAsync(cancellationToken);

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        _metrics.VoucherUnblocked();

        return new UnblockWorkerVoucherResult("Success");
    }
}
