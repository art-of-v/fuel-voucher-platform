using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetQrCodes;

public sealed class GetQrCodesQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetQrCodesQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<QrCodeDto>> HandleAsync(
        GetQrCodesQuery query,
        CancellationToken cancellationToken = default)
    {
        var items = await _context.FuelVouchers
            .AsNoTracking()
            .Include(v => v.FuelType)
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return items.Select((v, i) => new QrCodeDto
        {
            Id = i + 1,
            StationId = v.FuelType?.StationId ?? "",
            FuelType = v.FuelType?.Name ?? v.FuelTypeId,
            Liters = v.Liters,
            QrCodeUrl = v.QrPayload,
            Status = v.Status == VoucherStatus.Available ? "available" : "sold"
        }).ToList();
    }
}

public sealed class QrCodeDto
{
    public int Id { get; set; }
    public string StationId { get; set; } = null!;
    public string FuelType { get; set; } = null!;
    public decimal Liters { get; set; }
    public string? QrCodeUrl { get; set; }
    public string Status { get; set; } = null!;
}
