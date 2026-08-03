using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Vouchers.UpdateVoucher;

public sealed class UpdateVoucherCommandHandler
{
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

        if (newStatus == VoucherStatus.Assigned && !hasFulfillment)
        {
            return new UpdateVoucherResult
            {
                Success = false,
                Error = "Voucher cannot be assigned without an order fulfillment record — assign vouchers by fulfilling an order"
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
