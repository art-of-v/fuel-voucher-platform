using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Vouchers.UpdateVoucher;

public sealed class UpdateVoucherCommandHandler
{
    /// <summary>Statuses in which a voucher is in somebody's hands, i.e. owned by an order. Mirrors
    /// the <c>ck_voucher_held_has_order</c> CHECK on the table.</summary>
    private static readonly VoucherStatus[] HeldStatuses =
    [
        VoucherStatus.Assigned,
        VoucherStatus.Used,
        VoucherStatus.Blocked
    ];

    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public UpdateVoucherCommandHandler(
        ApplicationDbContext context,
        AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<UpdateVoucherResult?> HandleAsync(
        UpdateVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.FuelVouchers.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken);
        if (entity is null)
            return null;

        var oldStatus = entity.Status;
        var oldAssignedToUserId = entity.AssignedToUserId;

        var newStatus = entity.Status;
        if (!string.IsNullOrWhiteSpace(command.Status) && Enum.TryParse<VoucherStatus>(command.Status, out var parsedStatus))
            newStatus = parsedStatus;
        var newAssignedToUserId = command.AssignedToUserId ?? entity.AssignedToUserId;

        var hasFulfillment = await _context.Fulfillments.AnyAsync(f => f.VoucherId == entity.Id, cancellationToken);

        // A voucher only counts as handed over when an order says so. Without this guard an admin
        // could set Used or Blocked on warehouse stock and produce a voucher in somebody's hands
        // that no order explains - the same hole the database constraint now closes for good, so
        // the editor has to refuse it first and say why.
        if (HeldStatuses.Contains(newStatus) && entity.OrderId is null && !hasFulfillment)
        {
            return new UpdateVoucherResult
            {
                Success = false,
                Error = $"A voucher cannot be set to '{newStatus}' without an order — it belongs to nobody yet. Hand it over by fulfilling an order, renewing it, or issuing it to a worker"
            };
        }

        entity.Status = newStatus;
        if (command.AssignedToUserId.HasValue)
            entity.AssignedToUserId = newAssignedToUserId;

        var changedAtUtc = SystemClock.UtcNow;
        entity.UpdatedAtUtc = changedAtUtc;
        _context.FuelVouchers.Update(entity);

        // D-13. This is the only writer that can set any of the nine statuses, so anything it misses
        // here is missed everywhere: the editor is the back door around the instrumented flows.
        //
        // The from/to pair is compared rather than assumed. `request.Status` is a raw string the
        // admin UI sends verbatim, so POSTing the voucher's current status is entirely reachable, and
        // ck_voucher_events_status_changed would refuse `from == to` by aborting this very
        // SaveChangesAsync — turning a harmless re-save into an error.
        var actorId = command.ActingAdminUserId is { } actor && actor != Guid.Empty ? actor : (Guid?)null;
        if (oldStatus != newStatus && actorId is { } editor)
        {
            _context.VoucherEvents.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(),
                VoucherId = entity.Id,
                EventType = VoucherEventTypes.StatusChanged,
                FromStatus = oldStatus.ToString(),
                ToStatus = newStatus.ToString(),
                ActorKind = VoucherActorKinds.User,
                ActorUserId = editor,
                OccurredAtUtc = changedAtUtc
            });
        }

        if (command.ActingAdminUserId.HasValue)
        {
            _eventService.RecordEvent(
                "Voucher",
                entity.Id.ToString(),
                "VoucherUpdated",
                JsonSerializer.Serialize(new { status = oldStatus.ToString(), assignedToUserId = oldAssignedToUserId }),
                JsonSerializer.Serialize(new { status = entity.Status.ToString(), assignedToUserId = entity.AssignedToUserId }),
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Updated voucher {entity.VoucherNumber} ({entity.Brand})",
                entity.Brand);
        }

        // One save for the status change, the event and the audit row: the row and the change it
        // describes commit together, or neither does (planning #533).
        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateVoucherResult { Success = true };
    }
}

public sealed class UpdateVoucherResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
