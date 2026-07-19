using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.RestoreVoucher;

public sealed class RestoreVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;

    public RestoreVoucherCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RestoreVoucherResponse> HandleAsync(
        RestoreVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(v => v.Id == command.VoucherId, cancellationToken);

        if (voucher == null)
        {
            return new RestoreVoucherResponse(false, "Voucher not found");
        }

        if (voucher.Status == VoucherStatus.Assigned)
        {
            return new RestoreVoucherResponse(true, "Voucher is already in Assigned status");
        }

        if (voucher.Status != VoucherStatus.Used)
        {
            return new RestoreVoucherResponse(false, $"Voucher cannot be restored (current status: {voucher.Status})");
        }

        if (voucher.AssignedToUserId == null)
        {
            return new RestoreVoucherResponse(false, "Voucher has no assigned user");
        }

        voucher.Status = VoucherStatus.Assigned;
        voucher.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new RestoreVoucherResponse(true, "Voucher restored to Assigned status");
    }
}
