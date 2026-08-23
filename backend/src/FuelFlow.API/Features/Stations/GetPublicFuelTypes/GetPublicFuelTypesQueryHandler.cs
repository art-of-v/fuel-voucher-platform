using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicFuelTypes;

public sealed class GetPublicFuelTypesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicFuelTypesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PublicFuelTypeResponse>> HandleAsync(GetPublicFuelTypesQuery query, CancellationToken ct = default) =>
        await _context.FuelTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new PublicFuelTypeResponse(
                x.Id,
                x.Name,
                x.StationId,
                x.BasePrice,
                x.DiscountPrice))
            .ToListAsync(ct);
}
