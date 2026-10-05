using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
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
    private readonly ProviderEventService _eventService;

    public UpdateVoucherCommandHandler(
        ApplicationDbContext context,
        ProviderEventService eventService)
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

        entity.UpdatedAtUtc = DateTime.UtcNow;
        _context.FuelVouchers.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);

        if (command.ActingAdminUserId.HasValue)
        {
            await _eventService.RecordEventAsync(
                "Voucher",
                entity.Id.ToString(),
                "VoucherUpdated",
                JsonSerializer.Serialize(new { status = oldStatus.ToString(), assignedToUserId = oldAssignedToUserId }),
                JsonSerializer.Serialize(new { status = entity.Status.ToString(), assignedToUserId = entity.AssignedToUserId }),
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Updated voucher {entity.VoucherNumber} ({entity.Provider})",
                entity.Provider,
                cancellationToken);
        }

        return new UpdateVoucherResult { Success = true };
    }
}

public sealed class UpdateVoucherResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
