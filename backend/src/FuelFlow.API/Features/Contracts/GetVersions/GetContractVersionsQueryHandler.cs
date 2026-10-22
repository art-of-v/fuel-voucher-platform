using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetVersions;

/// <summary>
/// The versions of one provider's text, newest first.
/// </summary>
public sealed class GetContractVersionsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetContractVersionsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ContractVersionDto>> HandleAsync(
        GetContractVersionsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await _context.ContractVersions
            .AsNoTracking()
            .Where(v => v.BrandId == query.BrandId)
            .OrderByDescending(v => v.Version)
            .Select(v => new ContractVersionDto(
                v.Id,
                v.BrandId,
                v.Version,
                v.Body,
                v.TermDays,
                v.CreatedAtUtc,
                v.CreatedByUserId))
            .ToListAsync(cancellationToken);
    }
}
