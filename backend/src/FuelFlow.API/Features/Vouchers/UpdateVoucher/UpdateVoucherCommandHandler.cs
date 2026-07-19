using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.UpdateVoucher;

public sealed class UpdateVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateVoucherCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UpdateVoucherResult?> HandleAsync(
        UpdateVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.FuelVouchers.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken);
        if (entity is null)
            return null;

        if (!string.IsNullOrWhiteSpace(command.Status) && Enum.TryParse<VoucherStatus>(command.Status, out var parsedStatus))
            entity.Status = parsedStatus;
        if (command.AssignedToUserId.HasValue)
            entity.AssignedToUserId = command.AssignedToUserId;

        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateVoucherResult { Success = true };
    }
}

public sealed class UpdateVoucherResult
{
    public bool Success { get; set; }
}
