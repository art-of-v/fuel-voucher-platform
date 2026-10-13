using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Vouchers.DeleteVoucher;

public sealed class DeleteVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public DeleteVoucherCommandHandler(
        ApplicationDbContext context,
        AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<DeleteVoucherResult?> HandleAsync(
        DeleteVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.FuelVouchers.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken);
        if (entity is null)
            return null;

        // A paid voucher is never deleted. Soft-delete stopped the physical loss but also removed the
        // RESTRICT foreign key that used to make this throw by accident, so the rule is enforced
        // here instead - see VoucherDeletionGuard.
        if (VoucherDeletionGuard.IsPaidOrHeld(entity))
            return new DeleteVoucherResult { Success = false, Error = VoucherDeletionGuard.Reason(entity) };

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        _context.FuelVouchers.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);

        if (command.ActingAdminUserId.HasValue)
        {
            await _eventService.RecordEventAsync(
                "Voucher",
                entity.Id.ToString(),
                "VoucherDeleted",
                JsonSerializer.Serialize(new
                {
                    entity.VoucherNumber,
                    entity.Brand,
                    status = entity.Status.ToString(),
                    entity.AssignedToUserId
                }),
                "{}",
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Deleted voucher {entity.VoucherNumber} ({entity.Brand})",
                entity.Brand,
                cancellationToken);
        }

        return new DeleteVoucherResult { Success = true };
    }
}

public sealed class DeleteVoucherResult
{
    public bool Success { get; set; }

    /// <summary>Null on success; otherwise why the voucher was refused, written for the admin.</summary>
    public string? Error { get; set; }
}
