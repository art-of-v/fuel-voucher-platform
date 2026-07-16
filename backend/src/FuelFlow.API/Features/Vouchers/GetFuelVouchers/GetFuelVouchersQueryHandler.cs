using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetFuelVouchers;

public sealed class GetFuelVouchersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetFuelVouchersQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<FuelVoucherListResponse> HandleAsync(
        GetFuelVouchersQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.FuelVouchers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            q = q.Where(v =>
                v.Provider.Contains(query.Search) ||
                v.FuelTypeId.Contains(query.Search) ||
                v.VoucherNumber.Contains(query.Search));
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<VoucherStatus>(query.Status, out var parsedStatus))
            q = q.Where(v => v.Status == parsedStatus);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .AsNoTracking()
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new FuelVoucherListResponse
        {
            Items = items,
            Total = total
        };
    }
}

public sealed class FuelVoucherListResponse
{
    public List<FuelVoucher> Items { get; set; } = [];
    public int Total { get; set; }
}
