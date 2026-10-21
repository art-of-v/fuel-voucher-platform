using FuelFlow.API.Features.Orders.SharedServices;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.FireWorker;

public sealed record FireWorkerCommand(Guid OwnerUserId, Guid MemberId, Guid? LegalEntityId = null);

public sealed record FireWorkerResult(string Status, int BlockedVoucherCount = 0, string? ErrorMessage = null);

public sealed class FireWorkerCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public FireWorkerCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<FireWorkerResult> HandleAsync(FireWorkerCommand command, CancellationToken cancellationToken = default)
    {
        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new FireWorkerResult("CompanyNotOwned", ErrorMessage: "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new FireWorkerResult("OwnerCompanyNotFound", ErrorMessage: "Owner does not have a legal entity profile.");
        }

var member = await _context.CompanyMembers
        // AsTracking is required: the context default is TrackAll, so setting IsDeleted on an
        // untracked entity persists nothing and this handler would report Success having changed
        // nothing. Enforced by NoTrackingMutationGuardTests.
        .AsTracking()
        .FirstOrDefaultAsync(x => x.Id == command.MemberId && x.LegalEntityId == legalEntityId, cancellationToken);

        if (member is null)
        {
            return new FireWorkerResult("NotFound", ErrorMessage: "Company member not found.");
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        var assignedWorkerVouchers = await _context.FuelVouchers
            .Where(x =>
                x.LegalEntityId == legalEntityId &&
                x.WorkerUserId == member.WorkerUserId &&
                x.Status == VoucherStatus.Assigned)
            .ToListAsync(cancellationToken);

        // Fuel that leaves with the worker is company fuel again, so each voucher returns to the purchase it
        // was bought under. Vouchers already Blocked are not in the set above, so they keep their
        // issuance order: a frozen voucher stays the worker's, which is why the worker sees why it is
        // unusable.
        var restored = await IssuanceOrderLink.RestoreToPurchasesAsync(
            _context,
            assignedWorkerVouchers.Select(v => v.Id).ToList(),
            cancellationToken);

        foreach (var voucher in assignedWorkerVouchers)
        {
            // Captured before the rewrite below, for the movement's order_id: the recall is a
            // consequence of the handover that put this voucher in the worker's hands, not of the
            // purchase it now goes back to.
            var issuanceOrderId = voucher.OrderId;
            var firedAtUtc = SystemClock.UtcNow;

            voucher.WorkerUserId = null;
            voucher.Status = VoucherStatus.Blocked;
            if (restored.TryGetValue(voucher.Id, out var orderId))
                voucher.OrderId = orderId;
            voucher.UpdatedAtUtc = firedAtUtc;
            _context.Update(voucher);

            // D-13, and the one place in this flow that is BOTH a movement and an event — which is the
            // distinction the whole record turns on. The paper physically leaves the worker for the
            // company, so `recall` is owed; and it stops being redeemable, so the status changes too.
            // Neither row derives from the other.
            //
            // `recall` rather than `returned`, because the voucher returns to the company rather than
            // to the warehouse — the same reason the gift went company -> worker.
            _context.VoucherMovements.Add(new VoucherMovement
            {
                Id = Guid.CreateVersion7(),
                Kind = VoucherMovementKinds.Recall,
                VoucherId = voucher.Id,
                FromLocation = VoucherLocations.Worker,
                ToLocation = VoucherLocations.Company,
                OrderId = issuanceOrderId,
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.OwnerUserId,
                ActorPartyId = legalEntityId,
                OccurredAtUtc = firedAtUtc
            });

            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = voucher.Id,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = nameof(VoucherStatus.Assigned),
                ToStatus = nameof(VoucherStatus.Blocked),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.OwnerUserId,
                ActorPartyId = legalEntityId,
                OccurredAtUtc = firedAtUtc
            });
        }

        // Soft delete, not Remove. A membership is employment history: under the owner policy (#217)
        // "this person worked here until March" is a fact about the company that we keep forever,
        // and Remove destroyed it outright. The !IsDeleted query filter keeps the row out of every
        // membership read, and the unique index on worker_user_id is filtered the same way, so the
        // person can be invited again later.
        member.IsDeleted = true;
        member.DeletedAtUtc = SystemClock.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Recorded after commit so a rolled-back transaction cannot inflate the counter.
        if (assignedWorkerVouchers.Count > 0)
        {
            _metrics.VouchersBlocked(assignedWorkerVouchers.Count);
        }

        return new FireWorkerResult("Success", assignedWorkerVouchers.Count);
    }
}
