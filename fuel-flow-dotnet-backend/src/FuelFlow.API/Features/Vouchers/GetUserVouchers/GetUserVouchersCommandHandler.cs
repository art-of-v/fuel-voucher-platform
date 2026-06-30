using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetUserVouchers;

public sealed class GetUserVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;

    public GetUserVouchersCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetUserVouchersResponse> HandleAsync(
        GetUserVouchersCommand command,
        CancellationToken cancellationToken = default)
    {
        var vouchers = await _context.FuelVouchers
            .Where(v => v.AssignedToUserId == command.UserId)
            .Where(v => v.Status == VoucherStatus.Assigned || v.Status == VoucherStatus.Used)
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(v => new VoucherDto(
                v.Id,
                v.Provider,
                v.FuelTypeId,
                v.Liters,
                v.ExpirationDate,
                v.VoucherNumber,
                v.QrPayload,
                v.Status.ToString(),
                v.FuelSubtype,
                v.RedemptionRules,
                v.ImageUrl,
                v.CreatedAtUtc,
                v.UpdatedAtUtc
            ))
            .ToListAsync(cancellationToken);

        return new GetUserVouchersResponse(vouchers);
    }
}
