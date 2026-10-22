using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
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
            .IncludingSoftDeleted()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(v => v.FuelType)
            .Include(v => v.QrParameters)
            .Include(v => v.Supplier)
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
            FuelType = item.FuelType == null ? null : new FuelTypeRefDto { Id = item.FuelType.Id.ToString(), Name = item.FuelType.Name },
            Brand = item.Brand,
            SupplierId = item.SupplierId,
            SupplierName = item.Supplier?.Name,
            // Null stays null here for the reason it stays null in the list: nobody recorded what we
            // paid for this voucher, and a defaulted 0 would read as "this one was free".
            CostPerLiter = item.CostPerLiter,
            // Staff-facing detail: the real supplier term is what they plan against. The
            // customer-facing date is not relevant on a stock record.
            SupplierExpirationDate = item.SupplierExpirationDate,
            CustomerExpirationDate = item.CustomerExpirationDate,
            VoucherNumber = item.VoucherNumber,
            Status = item.Status.ToString(),
            CreatedAtUtc = item.CreatedAtUtc,
            QrImage = qrImage,
            // Was declared on the DTO and never assigned, so every detail response reported
            // isTestData: false. An emulated voucher then looked like paid-for stock on the one
            // screen where someone checks a single voucher before acting on it.
            IsTestData = item.DataKind == DataKinds.Qa
        };
    }
}
