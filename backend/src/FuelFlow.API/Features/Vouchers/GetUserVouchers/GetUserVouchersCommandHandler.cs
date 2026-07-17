using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetUserVouchers;

public sealed class GetUserVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IQrGenerator _qrGenerator;
    private readonly ILogger<GetUserVouchersCommandHandler> _logger;

    public GetUserVouchersCommandHandler(
        ApplicationDbContext context,
        IQrGenerator qrGenerator,
        ILogger<GetUserVouchersCommandHandler> logger)
    {
        _context = context;
        _qrGenerator = qrGenerator;
        _logger = logger;
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

        var vouchers = entities.Select(v =>
        {
            var qrImage = GenerateQrImage(v);
            return new VoucherDto(
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
                qrImage,
                v.CreatedAtUtc,
                v.UpdatedAtUtc
            );
        }).ToList();

        return new GetUserVouchersResponse(vouchers);
    }

    private string? GenerateQrImage(FuelVoucher voucher)
    {
        if (string.IsNullOrWhiteSpace(voucher.QrPayload))
        {
            _logger.LogWarning(
                "Voucher {VoucherId} ({VoucherNumber}) has no QR payload — cannot generate QR image",
                voucher.Id, voucher.VoucherNumber);
            return voucher.ImageUrl;
        }

        if (voucher.QrParameters is null)
        {
            _logger.LogWarning(
                "Voucher {VoucherId} ({VoucherNumber}) has no stored QR parameters — generated QR may differ from original",
                voucher.Id, voucher.VoucherNumber);
        }

        return "data:image/png;base64," + _qrGenerator.GenerateQrCode(
            voucher.QrPayload,
            eccLevel: voucher.QrParameters?.EccLevel,
            version: voucher.QrParameters?.Version,
            encodingMode: voucher.QrParameters?.EncodingMode,
            maskPattern: voucher.QrParameters?.MaskPattern);
    }
}
