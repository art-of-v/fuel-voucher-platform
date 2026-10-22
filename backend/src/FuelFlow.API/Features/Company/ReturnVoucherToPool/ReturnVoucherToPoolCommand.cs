using FuelFlow.API.Features.Orders.SharedServices;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.ReturnVoucherToPool;

public sealed record ReturnVoucherToPoolCommand(Guid OwnerUserId, Guid VoucherId, Guid? LegalEntityId = null);

public sealed record ReturnVoucherToPoolResult(string Status, string? ErrorMessage = null);

/// <summary>
/// The third owner action on a frozen voucher: put it back into the company pool, in one step.
/// <para>
/// A frozen voucher could only be thawed (<see cref="UnblockWorkerVoucher"/>), and a thaw gives the
/// paper back to whoever holds it. For a worker who no longer works here that is exactly the wrong
/// direction - the fuel is the company's, but the only road to the pool ran through the person who
/// was just fired, and they could spend it on the way. The frozen fuel of a firing also had no road
/// at all: <see cref="RecallVoucher"/> wants an <c>Assigned</c> voucher, the gift wants an
/// <c>Assigned</c> one in the pool, and the thaw wants a worker to thaw it for - so the litres were
/// counted as the company's stock and could not move.
/// </para>
/// <para>
/// This is that missing transition. The fuel goes back to the company pool and becomes spendable
/// company stock again - the same state a recall leaves, because it is the same fact: the paper is
/// the company's and the company may issue it to whoever it likes.
/// </para>
/// </summary>
public sealed class ReturnVoucherToPoolCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public ReturnVoucherToPoolCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<ReturnVoucherToPoolResult> HandleAsync(ReturnVoucherToPoolCommand command, CancellationToken cancellationToken = default)
    {
        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new ReturnVoucherToPoolResult("CompanyNotOwned", "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new ReturnVoucherToPoolResult("OwnerCompanyNotFound", "Owner does not have a legal entity profile.");
        }

        // Read-only guard, so AsNoTracking (#246). The transition itself is a compare-and-swap
        // ExecuteUpdateAsync further down, which bumps xmin behind EF's back; a tracked instance of
        // that same voucher would sit in the change tracker holding the pre-statement xmin, and the
        // first thing that later modified it would fail on a conflict that never happened.
        var voucher = await _context.FuelVouchers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == command.VoucherId, cancellationToken);

        if (voucher is null)
        {
            return new ReturnVoucherToPoolResult("NotFound", "Voucher not found.");
        }

        if (voucher.LegalEntityId != legalEntityId || voucher.AssignedToUserId != command.OwnerUserId)
        {
            return new ReturnVoucherToPoolResult("Forbidden", "Voucher does not belong to this company owner.");
        }

        // Only a frozen voucher is in this state at all. An active voucher in a worker's hands is
        // the recall's business (it never froze), and a spent one is history.
        if (voucher.Status != VoucherStatus.Blocked)
        {
            return new ReturnVoucherToPoolResult("InvalidState", "Only a frozen voucher can be returned to the company pool.");
        }

        // A voucher in somebody's hands always belongs to an order - ck_voucher_held_has_order says
        // so - and the pool needs it back under the purchase it arrived with.
        if (voucher.OrderId is not { } currentOrderId)
        {
            return new ReturnVoucherToPoolResult("InvalidState", "This voucher belongs to no order, so there is no purchase to file it under.");
        }

        // Captured before the restore below rewrites it: the movement's order_id is the order the
        // return is a *consequence of* - the handover that put the paper in the worker's hands -
        // the same way the recall names the gift and the firing names the handover.
        var issuanceOrderId = currentOrderId;

        // The fuel is company fuel again, so the voucher goes back under the purchase it arrived
        // with. Leaving it on the issuance order would keep the handover showing a voucher the
        // company no longer holds.
        var restored = await IssuanceOrderLink.RestoreToPurchasesAsync(
            _context,
            [voucher.Id],
            cancellationToken);
        var purchaseOrderId = restored.TryGetValue(voucher.Id, out var orderId) ? orderId : currentOrderId;

        // The return and the record of it commit together - see the same note in
        // BlockWorkerVoucherCommand. Without it a failed SaveChanges leaves an Assigned voucher
        // with nothing saying who released it.
        //
        // The transaction runs through ReplaySafeTransaction, so the execution strategy may replay
        // it: a transient failure retries the body rather than failing the request, and the retry
        // writes the movement and the event once.
        ReturnVoucherToPoolResult? lostRace = await _context.ExecuteReplaySafeAsync<ReturnVoucherToPoolResult?>(async ct =>
        {
            // Repeat the status predicate in the UPDATE so the return cannot race a thaw: exactly one
            // of a concurrent thaw / return wins. A redemption cannot race it at all - it wants an
            // Assigned voucher, and this one is Blocked until this statement lands.
            var returnedAtUtc = SystemClock.UtcNow;
            var affected = await _context.FuelVouchers
                .Where(v => v.Id == command.VoucherId && v.Status == VoucherStatus.Blocked)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(v => v.Status, VoucherStatus.Assigned)
                        .SetProperty(v => v.WorkerUserId, (Guid?)null)
                        .SetProperty(v => v.OrderId, purchaseOrderId)
                        .SetProperty(v => v.UpdatedAtUtc, returnedAtUtc),
                    ct);

            if (affected == 0)
            {
                // Nothing was written, so there is nothing to roll back: the empty transaction
                // commits as it stands and the caller is told the voucher was not returned.
                return new ReturnVoucherToPoolResult("InvalidState", "Only a frozen voucher can be returned to the company pool.");
            }

            // D-13. Two facts, and neither derives from the other.
            //
            // The movement - the paper goes from a worker's hands back to the company - is owed only
            // when there WAS a worker to take it from. A voucher the firing had already taken back is
            // already at the company, and a movement that says "company -> company" is not a movement
            // (ck_voucher_movements_distinct_locations). It is the same split the thaw makes: the thaw
            // writes an event and no movement, because a thaw moves no paper.
            if (voucher.WorkerUserId is not null)
            {
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
                    OccurredAtUtc = returnedAtUtc
                });
            }

            // The event - the voucher stops being frozen and becomes spendable company stock. That is a
            // change of what a ticket IS, not of where it is, which is what a voucher_events row is for.
            // The recall writes no event because a recall changes nothing about the voucher's kind.
            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = voucher.Id,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = nameof(VoucherStatus.Blocked),
                ToStatus = nameof(VoucherStatus.Assigned),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = command.OwnerUserId,
                ActorPartyId = legalEntityId,
                OccurredAtUtc = returnedAtUtc
            });

            await _context.SaveChangesAsync(ct);

            return null;
        }, cancellationToken: cancellationToken);

        if (lostRace is not null)
        {
            return lostRace;
        }

        _metrics.VoucherReturnedToPool();

        return new ReturnVoucherToPoolResult("Success");
    }
}
