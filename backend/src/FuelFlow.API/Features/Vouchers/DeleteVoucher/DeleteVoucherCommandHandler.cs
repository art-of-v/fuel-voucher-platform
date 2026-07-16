using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.DeleteVoucher;

public sealed class DeleteVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteVoucherCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DeleteVoucherResult?> HandleAsync(
        DeleteVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.FuelVouchers.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken);
        if (entity is null)
            return null;

        _context.FuelVouchers.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return new DeleteVoucherResult { Success = true };
    }
}

public sealed class DeleteVoucherResult
{
    public bool Success { get; set; }
}
