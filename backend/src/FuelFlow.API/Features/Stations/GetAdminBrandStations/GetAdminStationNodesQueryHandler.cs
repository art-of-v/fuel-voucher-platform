using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetAdminBrandStations;

public sealed class GetAdminBrandStationsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminBrandStationsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<PagedResult<BrandStation>> HandleAsync(GetAdminBrandStationsQuery query, CancellationToken ct = default)
    {
        var paged = new PagedRequest(query.Page, query.PageSize);
        var source = _context.BrandStations.AsNoTracking();

        if (query.BrandId is { } brandId && brandId != Guid.Empty)
            source = source.Where(n => n.BrandId == brandId);

        // The natural sort key, not the name. The names carry a number - "OKKO №01", "OKKO №10",
        // "OKKO №100" - and a byte sort puts them in the order "№01", "№09", "№1", "№10", "№100".
        // This list is paged, so the sort has to happen in the database: a sort applied after the
        // page was cut would put the same stations on two pages and leave others on none.
        //
        // The brand still leads, so the list stays grouped the way it was.
        return await source
            .OrderBy(n => n.BrandId)
            .ThenBy(n => EF.Property<string>(n, "NameSortKey"))
            .ThenBy(n => n.Name)
            .ToPagedResultAsync(paged, ct);
    }
}
