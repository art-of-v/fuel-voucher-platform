using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetLegalEntityProfile;

/// <summary>
/// Reads the caller's own legal entity.
/// </summary>
public sealed class GetLegalEntityProfileQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetLegalEntityProfileQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// The caller's own legal entity, or <c>null</c> when they have none yet.
    /// </summary>
    /// <param name="query">Which company, and for whom.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<LegalEntityDto?> HandleAsync(
        GetLegalEntityProfileQuery query,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.LegalEntities
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == query.UserId, cancellationToken);

        return entity is null ? null : LegalEntityDto.From(entity);
    }
}
