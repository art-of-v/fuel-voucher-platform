using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetMyLegalEntities;

/// <summary>
/// Lists the legal entities the caller owns.
/// </summary>
public sealed class GetMyLegalEntitiesQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetMyLegalEntitiesQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Every legal entity the caller owns, oldest first.
    /// </summary>
    /// <param name="query">Whose companies to list.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<List<LegalEntityDto>> HandleAsync(
        GetMyLegalEntitiesQuery query,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.LegalEntities
            .AsNoTracking()
            .Where(e => e.UserId == query.UserId)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return entities.Select(LegalEntityDto.From).ToList();
    }
}
