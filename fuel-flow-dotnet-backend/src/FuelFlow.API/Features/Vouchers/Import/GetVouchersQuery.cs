using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Import;

public sealed record GetVouchersQuery(int Page = 1, int PageSize = 50, string? FuelTypeId = null);

public sealed record VoucherDto(
    Guid Id,
    string Provider,
    string FuelTypeId,
    string FuelTypeName,
    decimal Liters,
    DateOnly ExpirationDate,
    string VoucherNumber,
    string QrPayload,
    string QrCodeBase64,
    string QrCodeUrl,
    DateTime CreatedAtUtc);

public sealed record GetVouchersResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<VoucherDto> Vouchers);

public sealed class GetVouchersQueryHandler
{
    private readonly IImportVouchersDbContext _context;
    private readonly IQrGenerator _qrGenerator;

    public GetVouchersQueryHandler(IImportVouchersDbContext context, IQrGenerator qrGenerator)
    {
        _context = context;
        _qrGenerator = qrGenerator;
    }

    public async Task<GetVouchersResponse> HandleAsync(GetVouchersQuery query, CancellationToken cancellationToken)
    {
        var queryable = _context.FuelVouchers.AsQueryable();

        if (!string.IsNullOrEmpty(query.FuelTypeId))
        {
            queryable = queryable.Where(v => v.FuelTypeId == query.FuelTypeId);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var vouchers = await queryable
            .OrderByDescending(v => v.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtoList = vouchers.Select(v => new VoucherDto(
            v.Id,
            v.Provider,
            v.FuelTypeId,
            v.FuelType?.Name ?? v.FuelTypeId,
            v.Liters,
            v.ExpirationDate,
            v.VoucherNumber,
            v.QrPayload,
            _qrGenerator.GenerateQrCode(v.QrPayload),
            $"/api/Vouchers/{v.Id}/qr",
            v.CreatedAtUtc)).ToList();

        return new GetVouchersResponse(totalCount, query.Page, query.PageSize, dtoList);
    }

    public async Task<FuelVoucher?> GetVoucherByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.FuelVouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }
}