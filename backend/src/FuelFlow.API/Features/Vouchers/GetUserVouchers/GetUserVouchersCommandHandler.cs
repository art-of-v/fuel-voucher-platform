using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetUserVouchers;

public sealed class GetUserVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IQrGenerator _qrGenerator;

    public GetUserVouchersCommandHandler(
        ApplicationDbContext context,
        IQrGenerator qrGenerator)
    {
        _context = context;
        _qrGenerator = qrGenerator;
    }

    public async Task<GetUserVouchersResponse> HandleAsync(
        GetUserVouchersCommand command,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.FuelVouchers
            .Include(v => v.QrParameters)
            .Where(v => v.AssignedToUserId == command.UserId)
            .Where(v => v.Status == VoucherStatus.Assigned || v.Status == VoucherStatus.Used)
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var vouchers = entities.Select(v => new VoucherDto(
            v.Id,
            v.Provider,
            v.FuelTypeId,
            v.Liters,
            v.Liters,
            v.ExpirationDate,
            v.VoucherNumber,
            v.VoucherNumber,
            v.QrPayload,
            v.QrPayload,
            v.Status.ToString(),
            v.FuelSubtype,
            v.RedemptionRules,
            GenerateQrImage(v.QrPayload, v.QrParameters) ?? v.ImageUrl,
            v.CreatedAtUtc,
            v.UpdatedAtUtc
        )).ToList();

        return new GetUserVouchersResponse(vouchers);
    }

    private string? GenerateQrImage(string? qrPayload, QrParameters? qrParams)
    {
        if (string.IsNullOrWhiteSpace(qrPayload))
            return null;

        return "data:image/png;base64," + _qrGenerator.GenerateQrCode(
            qrPayload,
            eccLevel: qrParams?.EccLevel,
            version: qrParams?.Version,
            encodingMode: qrParams?.EncodingMode,
            maskPattern: qrParams?.MaskPattern);
    }
}
