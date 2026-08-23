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
        var q = _context.FuelVouchers.IgnoreQueryFilters().AsQueryable();

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

        // Clamp server-side. This query calls IgnoreQueryFilters() above and projects raw
        // FuelVoucher entities, VoucherNumber included, so an unbounded PageSize turned a
        // paged inventory screen into a single-request dump of every redeemable voucher
        // code, and made the server materialize the whole table into memory. Ordering also
        // has to be applied BEFORE Skip/Take: with OrderByDescending after them, EF emits
        // OFFSET/LIMIT over an unordered relation, and Postgres is then free to return rows
        // in any order per page - so a voucher could be absent from every page of an
        // inventory list that admins use to reconcile stock.
        var page = PageLimits.ClampPage(query.Page);
        var pageSize = PageLimits.ClampPageSize(query.PageSize);

        var items = await q
            .AsNoTracking()
            .OrderByDescending(v => v.CreatedAtUtc)
            .Skip(PageLimits.SkipFor(page, pageSize))
            .Take(pageSize)
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
