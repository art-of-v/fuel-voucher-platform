using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.UnarchiveVoucher;

/// <summary>
/// Puts an archived voucher back on the board. The missing undo for the soft delete introduced in
/// #217 - <c>BulkActionVouchersCommandHandler</c>'s <c>delete_all</c> flags every row, and until this
/// existed there was no way back: a mistaken bulk delete was permanent through the UI.
/// </summary>
public sealed class UnarchiveVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public UnarchiveVoucherCommandHandler(
        ApplicationDbContext context,
        AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    /// <summary>
    /// Returns <c>null</c> when the voucher does not exist at all, so the controller can answer 404.
    /// A voucher that is already visible is a success, not an error - the operator's intent is
    /// already satisfied and re-clicking should not look like a failure.
    /// </summary>
    public async Task<UnarchiveVoucherResult?> HandleAsync(
        UnarchiveVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters is the whole point: the row being brought back is exactly the one the
        // global !IsDeleted filter hides, so an ordinary lookup would report it missing.
        var voucher = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.Id == command.VoucherId)
            .Select(v => new { v.IsDeleted, v.VoucherNumber, v.Brand, v.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (voucher is null)
        {
            return null;
        }

        if (!voucher.IsDeleted)
        {
            return new UnarchiveVoucherResult { Success = true };
        }

        // Only the flag is cleared. Status is deliberately left alone: this undoes a *deletion*, not
        // a redemption, and a voucher archived while Available must come back Available rather than
        // being quietly promoted by whoever restored it. That is the difference between this and
        // RestoreVoucherCommandHandler, which is a redemption undo and does move the status.
        //
        // The predicate is repeated in the UPDATE's WHERE clause so a concurrent second call loses
        // cleanly instead of double-reporting a restore that only happened once.
        // The flag flip and the record of it are one fact. `ExecuteUpdateAsync` leaves the change
        // tracker untouched and commits on its own, so without this the un-archive could land with a
        // failed SaveChanges leaving nothing naming who did it — the exact action-without-an-author the
        // actor constraints exist to prevent. Guarded by IsRelational so the in-memory unit brand,
        // which has no transactions and no ExecuteUpdate, is unaffected.
        await using var tx = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var affected = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.Id == command.VoucherId && v.IsDeleted)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.IsDeleted, false)
                    .SetProperty(v => v.UpdatedAtUtc, SystemClock.UtcNow),
                cancellationToken);

        if (affected == 0)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(cancellationToken);
            }

            return new UnarchiveVoucherResult { Success = true };
        }

        // D-13. `Unarchived`, not `StatusChanged`: the comment above explains why status is left
        // alone, and writing a StatusChanged here would assert a transition the statement did not
        // make. A voucher can be archived while sitting in any state, so from/to would be a
        // fabrication rather than a record.
        //
        // Written after the `affected == 0` return above, so the idempotent path records nothing:
        // one un-archive, one row.
        var actorId = command.ActingAdminUserId is { } actor && actor != Guid.Empty ? actor : (Guid?)null;
        if (actorId is { } unarchiveActor)
        {
            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = command.VoucherId,
                EventType = VoucherEventTypes.Unarchived,
                ActorKind = VoucherActorKinds.User,
                ActorUserId = unarchiveActor,
                OccurredAtUtc = SystemClock.UtcNow
            });

            await _context.SaveChangesAsync(cancellationToken);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        if (command.ActingAdminUserId.HasValue)
        {
            await _eventService.RecordEventAsync(
                "Voucher",
                voucher.VoucherNumber,
                "VoucherUnarchived",
                JsonSerializer.Serialize(new { isDeleted = true, status = voucher.Status.ToString() }),
                JsonSerializer.Serialize(new { isDeleted = false, status = voucher.Status.ToString() }),
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Unarchived voucher {voucher.VoucherNumber} ({voucher.Brand})",
                voucher.Brand,
                cancellationToken);
        }

        return new UnarchiveVoucherResult { Success = true };
    }
}

public sealed class UnarchiveVoucherResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}