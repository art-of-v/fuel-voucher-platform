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

        return await source
            .OrderBy(n => n.BrandId)
            .ThenBy(n => n.Name)
            .ToPagedResultAsync(paged, ct);
    }
}
