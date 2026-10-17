using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.MarkVoucherAsUsed;

public sealed class MarkVoucherAsUsedCommandHandler
{
    private readonly ApplicationDbContext _context;

    public MarkVoucherAsUsedCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MarkVoucherAsUsedResponse> HandleAsync(
        MarkVoucherAsUsedCommand command,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Id == command.VoucherId)
            .Select(v => new { v.Status, v.WorkerUserId, v.AssignedToUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (voucher == null)
        {
            return new MarkVoucherAsUsedResponse(false, "Voucher not found", "NotFound");
        }

        // Same rule, and the same wording, as the endpoints that hand out the QR: VoucherHolder is the one
        // answer to "may this user redeem it", so this write can never permit what a read refuses.
        if (!VoucherHolder.CanRedeem(voucher.WorkerUserId, voucher.AssignedToUserId, command.UserId))
        {
            return new MarkVoucherAsUsedResponse(
                false,
                voucher.WorkerUserId.HasValue
                    ? "Only the assigned worker can use this voucher"
                    : "Voucher is not assigned to this user",
                "Forbidden");
        }

        if (voucher.Status == VoucherStatus.Used)
        {
            return new MarkVoucherAsUsedResponse(true, "Voucher already marked as used");
        }

        if (voucher.Status != VoucherStatus.Assigned)
        {
            return new MarkVoucherAsUsedResponse(false, $"Voucher cannot be marked as used (current status: {voucher.Status})", "InvalidState");
        }

        // The status check above is advisory only: between it and the write, another request
        // could redeem the same voucher. Repeating the predicate in the UPDATE's WHERE clause
        // makes the transition atomic, so exactly one of two concurrent redemptions wins.
        //
        // One instant for both writes. The stamp that lands on the voucher and the one on the event
        // have to be the same moment, or "when was this redeemed" answers differently depending on
        // which table you ask - and the event is the one an audit reads.
        var occurredAtUtc = DateTime.UtcNow;

        // The transition and the record of it are one fact, so they commit together. Without the
        // transaction a failed SaveChanges leaves status = Used with nothing naming who did it,
        // which is exactly the action-without-an-author the actor CHECKs exist to prevent.
        await using var tx = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var affected = await _context.FuelVouchers
            .Where(v => v.Id == command.VoucherId && v.Status == VoucherStatus.Assigned)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.Used)
                    // Stamped inside the same UPDATE that performs the transition, so the redemption
                    // time is the one that won the race rather than whatever a caller decided earlier.
                    // This is the "when used" a worker usage report reads (#150).
                    .SetProperty(v => v.UsedAtUtc, occurredAtUtc)
                    .SetProperty(v => v.UpdatedAtUtc, occurredAtUtc),
                cancellationToken);

        if (affected == 0)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(cancellationToken);
            }

            // Lost the race. Re-read so the caller is told the real outcome rather than a stale one.
            var current = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.Id == command.VoucherId)
                .Select(v => (VoucherStatus?)v.Status)
                .FirstOrDefaultAsync(cancellationToken);

            return current switch
            {
                VoucherStatus.Used => new MarkVoucherAsUsedResponse(true, "Voucher already marked as used"),
                null => new MarkVoucherAsUsedResponse(false, "Voucher not found", "NotFound"),
                _ => new MarkVoucherAsUsedResponse(false, $"Voucher cannot be marked as used (current status: {current})", "InvalidState")
            };
        }

        // A redemption moves no paper. The customer is holding the same ticket they were holding a
        // moment ago; it has been spent, not relocated - so this is an event and not a movement, and
        // the voucher's status is the only thing that changed, which is what from/to record.
        //
        // Written only after the CAS reports it won. The idempotent branches above and the lost-race
        // branch deliberately write nothing: a second mark-as-used is not a second redemption, and
        // recording it would put two rows on one physical handover.
        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = command.VoucherId,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.Assigned),
            ToStatus = nameof(VoucherStatus.Used),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = command.UserId,
            OccurredAtUtc = occurredAtUtc
        });

        await _context.SaveChangesAsync(cancellationToken);

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return new MarkVoucherAsUsedResponse(true, "Voucher marked as used");
    }
}
