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
            .AsNoTracking()
            .Where(v => v.Id == command.VoucherId)
            .Select(v => new { v.Status, v.WorkerUserId, v.AssignedToUserId })
            .FirstOrDefaultAsync(cancellationToken);

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

        // The status check above is advisory only: between it and the write, another request
        // could redeem the same voucher. Repeating the predicate in the UPDATE's WHERE clause
        // makes the transition atomic, so exactly one of two concurrent redemptions wins.
        var affected = await _context.FuelVouchers
            .Where(v => v.Id == command.VoucherId && v.Status == VoucherStatus.Assigned)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.Used)
                    .SetProperty(v => v.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken);

        if (affected == 0)
        {
            // Lost the race. Re-read so the caller is told the real outcome rather than a stale one.
            var current = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.Id == command.VoucherId)
                .Select(v => (VoucherStatus?)v.Status)
                .FirstOrDefaultAsync(cancellationToken);

            return current switch
            {
                VoucherStatus.Used => new MarkVoucherAsUsedResponse(true, "Voucher already marked as used"),
                null => new MarkVoucherAsUsedResponse(false, "Voucher not found", "NotFound"),
                _ => new MarkVoucherAsUsedResponse(false, $"Voucher cannot be marked as used (current status: {current})", "InvalidState")
            };
        }

        return new MarkVoucherAsUsedResponse(true, "Voucher marked as used");
    }
}
