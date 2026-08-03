using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Vouchers.DeleteVoucher;

public sealed class DeleteVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ProviderEventService _eventService;

    public DeleteVoucherCommandHandler(
        ApplicationDbContext context,
        ProviderEventService eventService)
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
                    entity.Provider,
                    status = entity.Status.ToString(),
                    entity.AssignedToUserId
                }),
                "{}",
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Deleted voucher {entity.VoucherNumber} ({entity.Provider})",
                entity.Provider,
                cancellationToken);
        }

        return new DeleteVoucherResult { Success = true };
    }
}

public sealed class DeleteVoucherResult
{
    public bool Success { get; set; }
}
