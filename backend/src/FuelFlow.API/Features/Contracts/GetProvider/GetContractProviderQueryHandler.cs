using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetProvider;

/// <summary>
/// The provider with a given key, or null when there is no such provider.
/// </summary>
/// <remarks>
/// The screen asks this before it writes: a text for a provider that does not exist is a text
/// nobody can sign, and the operator is told "Provider not found" rather than being shown a
/// version list that belongs to nobody.
/// </remarks>
public sealed class GetContractProviderQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetContractProviderQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Guid?> HandleAsync(
        GetContractProviderQuery query,
        CancellationToken cancellationToken = default)
    {
        var brand = await _context.Brands
            .AsNoTracking()
            .Where(b => b.Id == query.BrandId)
            .Select(b => new { b.Id })
            .FirstOrDefaultAsync(cancellationToken);

        return brand?.Id;
    }
}
