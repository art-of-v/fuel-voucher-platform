using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Import;

public sealed record GetVouchersQuery(int Page = 1, int PageSize = 50, string? FuelTypeId = null);

/// <summary>
/// Admin catalog list row. Deliberately carries NO redeemable material: the QR payload is
/// the bearer instrument a station scans, so it is never included in a list response.
/// Fetch it one voucher at a time via <see cref="QrCodeUrl"/>, which enforces admin-or-owner.
/// </summary>
public sealed record VoucherDto(
    Guid Id,
    string Provider,
    string FuelTypeId,
    string FuelTypeName,
    decimal Liters,
    DateOnly ExpirationDate,
    string VoucherNumber,
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

    public const int MaxPageSize = 200;

    public async Task<GetVouchersResponse> HandleAsync(GetVouchersQuery query, CancellationToken cancellationToken)
    {
        // Clamp server-side: an unbounded PageSize is both a bulk-exfiltration lever and,
        // because every row used to render a QR image, a CPU/memory amplifier.
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var queryable = _context.FuelVouchers.AsQueryable();

        if (!string.IsNullOrEmpty(query.FuelTypeId))
        {
            queryable = queryable.Where(v => v.FuelTypeId == query.FuelTypeId);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var vouchers = await queryable
            .OrderByDescending(v => v.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(v => v.QrParameters)
            .ToListAsync(cancellationToken);

        var dtoList = vouchers.Select(v => new VoucherDto(
            v.Id,
            v.Provider,
            v.FuelTypeId,
            v.FuelType?.Name ?? v.FuelTypeId,
            v.Liters,
            v.ExpirationDate,
            v.VoucherNumber,
            $"/api/Vouchers/{v.Id}/qr",
            v.CreatedAtUtc)).ToList();

        return new GetVouchersResponse(totalCount, page, pageSize, dtoList);
    }

    public async Task<FuelVoucher?> GetVoucherByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.FuelVouchers
            .Include(v => v.QrParameters)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }
}