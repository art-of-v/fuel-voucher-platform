using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetVoucherVerification;

public sealed class GetVoucherVerificationQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IQrGenerator _qrGenerator;

    public GetVoucherVerificationQueryHandler(ApplicationDbContext context, IQrGenerator qrGenerator)
    {
        _context = context;
        _qrGenerator = qrGenerator;
    }

    public async Task<GetVoucherVerificationResponse?> HandleAsync(
        GetVoucherVerificationQuery query,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .Include(v => v.QrParameters)
            .FirstOrDefaultAsync(v => v.Id == query.VoucherId, cancellationToken);

        if (voucher == null)
            return null;

        var qrBase64 = _qrGenerator.GenerateQrCode(
            voucher.QrPayload,
            eccLevel: voucher.QrParameters?.EccLevel,
            version: voucher.QrParameters?.Version,
            encodingMode: voucher.QrParameters?.EncodingMode,
            maskPattern: voucher.QrParameters?.MaskPattern);

        return new GetVoucherVerificationResponse(
            voucher.Id,
            voucher.VoucherNumber,
            voucher.Status.ToString(),
            voucher.VerificationMismatchPercent,
            voucher.VerificationMismatchedModules,
            voucher.VerificationTotalModules,
            $"data:image/png;base64,{qrBase64}");
    }
}
