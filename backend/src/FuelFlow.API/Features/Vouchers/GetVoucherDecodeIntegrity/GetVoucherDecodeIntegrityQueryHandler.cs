using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetVoucherDecodeIntegrity;

public sealed class GetVoucherDecodeIntegrityQueryHandler
{
    /// <summary>
    /// No supplier signature, serial or registry check exists in the payload, so the supplier is only ever
    /// the operator's declaration. The API says so on every response instead of letting a matrix match read
    /// as authenticity.
    /// </summary>
    public const string SupplierProvenanceUnverified = "Unverified";

    private readonly ApplicationDbContext _context;
    private readonly IQrGenerator _qrGenerator;

    public GetVoucherDecodeIntegrityQueryHandler(ApplicationDbContext context, IQrGenerator qrGenerator)
    {
        _context = context;
        _qrGenerator = qrGenerator;
    }

    public async Task<GetVoucherDecodeIntegrityResponse?> HandleAsync(
        GetVoucherDecodeIntegrityQuery query,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .AsNoTracking()
            .Include(v => v.QrParameters)
            .Include(v => v.Supplier)
            .FirstOrDefaultAsync(v => v.Id == query.VoucherId, cancellationToken);

        if (voucher == null)
            return null;

        var qrBase64 = _qrGenerator.GenerateQrCode(
            voucher.QrPayload,
            eccLevel: voucher.QrParameters?.EccLevel,
            version: voucher.QrParameters?.Version,
            encodingMode: voucher.QrParameters?.EncodingMode,
            maskPattern: voucher.QrParameters?.MaskPattern);

        return new GetVoucherDecodeIntegrityResponse(
            voucher.Id,
            voucher.VoucherNumber,
            voucher.Status.ToString(),
            Outcome(voucher.MatrixMismatchPercent),
            voucher.MatrixMismatchPercent,
            voucher.MatrixMismatchedModules,
            voucher.MatrixTotalModules,
            voucher.SupplierId,
            voucher.Supplier?.Name,
            SupplierProvenanceUnverified,
            $"data:image/png;base64,{qrBase64}");
    }

    /// <summary>
    /// Read from the recorded percentage rather than the current status: a voucher can move on through its
    /// lifecycle, and the result of the check must not change with it.
    /// </summary>
    private static string Outcome(decimal? mismatchPercent) => mismatchPercent switch
    {
        null => "NotChecked",
        var percent when percent > (decimal)QrDecodeIntegrityVerifier.MismatchThresholdPercent => "Mismatch",
        var percent when percent > (decimal)QrDecodeIntegrityVerifier.WarningThresholdPercent => "Warning",
        _ => "Matched"
    };
}
