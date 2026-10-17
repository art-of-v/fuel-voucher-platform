using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.RestoreVoucher;

public sealed class RestoreVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;

    public RestoreVoucherCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Undoes a customer's own redemption: <c>Used</c> back to <c>Assigned</c>. The mirror image of
    /// <see cref="MarkVoucherAsUsed.MarkVoucherAsUsedCommandHandler"/> under
    /// <c>FuelFlow.Features.Vouchers</c>, and deliberately built the
    /// same way rather than patched - the previous version had neither an ownership check nor an
    /// atomic transition, and only the endpoint's <c>Staff</c> policy was keeping it from acting on
    /// other people's vouchers.
    /// <para>
    /// This is <b>not</b> an un-delete. The global <c>!IsDeleted</c> query filter stays in force here,
    /// so an archived voucher is invisible and returns <c>NotFound</c> on purpose: un-archiving is an
    /// operator action and lives behind the admin endpoint, not something a customer can undo by id.
    /// </para>
    /// </summary>
    public async Task<RestoreVoucherResponse> HandleAsync(
        RestoreVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Id == command.VoucherId)
            .Select(v => new { v.Status, v.WorkerUserId, v.AssignedToUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (voucher == null)
        {
            return new RestoreVoucherResponse(false, "Voucher not found", "NotFound");
        }

        // Same rule, and the same wording, as MarkVoucherAsUsed and the endpoints that hand out the QR.
        if (!VoucherHolder.CanRedeem(voucher.WorkerUserId, voucher.AssignedToUserId, command.UserId))
        {
            return new RestoreVoucherResponse(
                false,
                voucher.WorkerUserId.HasValue
                    ? "Only the assigned worker can restore this voucher"
                    : "Voucher is not assigned to this user",
                "Forbidden");
        }

        if (voucher.Status == VoucherStatus.Assigned)
        {
            return new RestoreVoucherResponse(true, "Voucher is already in Assigned status");
        }

        if (voucher.Status != VoucherStatus.Used)
        {
            return new RestoreVoucherResponse(false, $"Voucher cannot be restored (current status: {voucher.Status})", "InvalidState");
        }

        // The status check above is advisory only: between it and the write, another request could
        // restore the same voucher. Repeating the predicate in the UPDATE's WHERE clause makes the
        // transition atomic, so exactly one of two concurrent restores wins.
        //
        // One instant for both writes, for the same reason as MarkVoucherAsUsed: the voucher's stamp
        // and the event's have to agree or the two tables answer "when" differently.
        var occurredAtUtc = DateTime.UtcNow;

        // The transition and the record of it are one fact, so they commit together - see the same
        // note in MarkVoucherAsUsedCommandHandler.
        await using var tx = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var affected = await _context.FuelVouchers
            .Where(v => v.Id == command.VoucherId && v.Status == VoucherStatus.Used)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.Assigned)
                    // Cleared in the same statement that undoes the redemption. UsedAtUtc answers
                    // "when was this redeemed", and leaving it set hands the voucher a redemption
                    // that no longer happened - which a worker usage report would then count.
                    .SetProperty(v => v.UsedAtUtc, (DateTime?)null)
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
                VoucherStatus.Assigned => new RestoreVoucherResponse(true, "Voucher is already in Assigned status"),
                null => new RestoreVoucherResponse(false, "Voucher not found", "NotFound"),
                _ => new RestoreVoucherResponse(false, $"Voucher cannot be restored (current status: {current})", "InvalidState")
            };
        }

        // A restore moves no paper either. The customer's ticket never left them, so undoing the
        // redemption returns no voucher to any pool - which is why there is no `returned` movement
        // here, and why this is the exact inverse of the mark-used event rather than a new shape.
        //
        // Written only after the CAS reports it won, so the idempotent and lost-race branches record
        // nothing: one handover, one row.
        _context.VoucherEvents.Add(new VoucherEvent
        {
            Id = Guid.NewGuid(),
            VoucherId = command.VoucherId,
            EventType = VoucherEventTypes.StatusChanged,
            FromStatus = nameof(VoucherStatus.Used),
            ToStatus = nameof(VoucherStatus.Assigned),
            ActorKind = VoucherActorKinds.User,
            ActorUserId = command.UserId,
            OccurredAtUtc = occurredAtUtc
        });

        await _context.SaveChangesAsync(cancellationToken);

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return new RestoreVoucherResponse(true, "Voucher restored to Assigned status");
    }
}