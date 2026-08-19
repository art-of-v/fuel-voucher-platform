using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.MarkVoucherAsUsed;

public sealed class MarkVoucherAsUsedCommandHandler
{
    private readonly ApplicationDbContext _context;

    public MarkVoucherAsUsedCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MarkVoucherAsUsedResponse> HandleAsync(
        MarkVoucherAsUsedCommand command,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(v => v.Id == command.VoucherId, cancellationToken);

        if (voucher == null)
        {
            return new MarkVoucherAsUsedResponse(false, "Voucher not found", "NotFound");
        }

        if (voucher.WorkerUserId.HasValue)
        {
            if (voucher.WorkerUserId != command.UserId)
            {
                return new MarkVoucherAsUsedResponse(false, "Only the assigned worker can use this voucher", "Forbidden");
            }
        }
        else if (voucher.AssignedToUserId != command.UserId)
        {
            return new MarkVoucherAsUsedResponse(false, "Voucher is not assigned to this user", "Forbidden");
        }

        if (voucher.Status == VoucherStatus.Used)
        {
            return new MarkVoucherAsUsedResponse(true, "Voucher already marked as used");
        }

        if (voucher.Status != VoucherStatus.Assigned)
        {
            return new MarkVoucherAsUsedResponse(false, $"Voucher cannot be marked as used (current status: {voucher.Status})", "InvalidState");
        }

        voucher.Status = VoucherStatus.Used;
        voucher.UpdatedAtUtc = DateTime.UtcNow;
        _context.FuelVouchers.Update(voucher);

        await _context.SaveChangesAsync(cancellationToken);

        return new MarkVoucherAsUsedResponse(true, "Voucher marked as used");
    }
}
