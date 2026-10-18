using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetAdminBrands;

public sealed class GetAdminBrandsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminBrandsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<PagedResult<Brand>> HandleAsync(GetAdminBrandsQuery query, CancellationToken ct = default)
    {
        var paged = new PagedRequest(query.Page, query.PageSize);
        return await _context.Brands
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToPagedResultAsync(paged, ct);
    }
}
