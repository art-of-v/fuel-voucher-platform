using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetAdminVoucherById;

public sealed class GetAdminVoucherByIdQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IQrGenerator _qrGenerator;

    public GetAdminVoucherByIdQueryHandler(ApplicationDbContext context, IQrGenerator qrGenerator)
    {
        _context = context;
        _qrGenerator = qrGenerator;
    }

    public async Task<AdminVoucherDetailDto?> HandleAsync(
        GetAdminVoucherByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.FuelVouchers
            .AsNoTracking()
            .Include(v => v.FuelType)
            .Include(v => v.QrParameters)
            .FirstOrDefaultAsync(v => v.Id == query.Id, cancellationToken);

        if (item is null)
            return null;

        var qrImage = string.IsNullOrWhiteSpace(item.QrPayload)
            ? null
            : "data:image/png;base64," + _qrGenerator.GenerateQrCode(
                item.QrPayload,
                eccLevel: item.QrParameters?.EccLevel,
                version: item.QrParameters?.Version,
                encodingMode: item.QrParameters?.EncodingMode,
                maskPattern: item.QrParameters?.MaskPattern);

        return new AdminVoucherDetailDto
        {
            Id = item.Id,
            QrPayload = item.QrPayload,
            Liters = item.Liters,
            FuelTypeId = item.FuelTypeId,
            FuelType = item.FuelType == null ? null : new FuelTypeRefDto { Id = item.FuelType.Id, Name = item.FuelType.Name },
            Provider = item.Provider,
            ExpirationDate = item.ExpirationDate,
            VoucherNumber = item.VoucherNumber,
            Status = item.Status.ToString(),
            CreatedAtUtc = item.CreatedAtUtc,
            ImageUrl = item.ImageUrl,
            QrImage = qrImage
        };
    }
}
