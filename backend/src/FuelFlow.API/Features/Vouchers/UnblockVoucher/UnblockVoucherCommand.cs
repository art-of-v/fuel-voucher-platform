using System.Text.Json;
using FuelFlow.API.Features.Orders.SharedServices;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.UnblockVoucher;

public sealed record UnblockVoucherCommand(
    Guid VoucherId,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null);

public sealed class UnblockVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public UnblockVoucherCommandHandler(
        ApplicationDbContext context,
        AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<UnblockVoucherResult?> HandleAsync(
        UnblockVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(v => v.Id == command.VoucherId, cancellationToken);

        if (voucher is null)
        {
            return null;
        }

        if (voucher.Status != VoucherStatus.Blocked)
        {
            return new UnblockVoucherResult
            {
                Success = false,
                Error = $"Only blocked vouchers can be unblocked (current status: {voucher.Status})"
            };
        }

        var oldStatus = voucher.Status;
        var oldWorkerUserId = voucher.WorkerUserId;

        // Captured before the restore rewrites it, so the movement points at the handover it is undoing
        // rather than the purchase the voucher lands back on.
        var issuanceOrderId = voucher.OrderId;

        // Unblocking drops the worker link, and a voucher without a worker is company stock again — so it
        // returns to its purchase rather than staying on an issuance order that no longer describes
        // it. Same rule as recall and firing; all three paths have to agree.
        var restored = await IssuanceOrderLink.RestoreToPurchasesAsync(
            _context,
            [voucher.Id],
            cancellationToken);

        var unblockedAtUtc = DateTime.UtcNow;
        voucher.Status = VoucherStatus.Assigned;
        voucher.WorkerUserId = null;
        if (restored.TryGetValue(voucher.Id, out var orderId))
            voucher.OrderId = orderId;
        voucher.UpdatedAtUtc = unblockedAtUtc;

        _context.FuelVouchers.Update(voucher);

        // D-13. Unlike the owner-side unblock, this one DOES move the paper: clearing the worker link
        // takes the voucher out of a worker's hands and back to the company. That is why this path owes
        // a movement while `UnblockWorkerVoucherCommand` owes only an event — the difference is visible
        // in the data and is worth being able to read.
        //
        // Guarded on the worker actually having been set, so unblocking a voucher that was blocked
        // without a worker does not invent a custody row for paper that never left the company.
        var adminId = command.ActingAdminUserId is { } actor && actor != Guid.Empty ? actor : (Guid?)null;
        if (oldWorkerUserId is not null && adminId is { } unblockedBy)
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
                ActorUserId = unblockedBy,
                OccurredAtUtc = unblockedAtUtc
            });

            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = voucher.Id,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = oldStatus.ToString(),
                ToStatus = nameof(VoucherStatus.Assigned),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = unblockedBy,
                OccurredAtUtc = unblockedAtUtc
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (command.ActingAdminUserId.HasValue)
        {
            await _eventService.RecordEventAsync(
                "Voucher",
                voucher.Id.ToString(),
                "VoucherUnblocked",
                JsonSerializer.Serialize(new { status = oldStatus.ToString(), workerUserId = oldWorkerUserId }),
                JsonSerializer.Serialize(new { status = voucher.Status.ToString(), workerUserId = voucher.WorkerUserId }),
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Unblocked voucher {voucher.VoucherNumber} ({voucher.Brand})",
                voucher.Brand,
                cancellationToken);
        }

        return new UnblockVoucherResult { Success = true };
    }
}

public sealed class UnblockVoucherResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
