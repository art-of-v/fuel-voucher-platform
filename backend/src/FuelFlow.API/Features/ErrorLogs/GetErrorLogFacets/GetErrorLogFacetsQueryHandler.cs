using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.ErrorLogs.GetErrorLogFacets;

/// <summary>
/// The distinct levels and sources of the error journal.
/// </summary>
/// <remarks>
/// The sources are capped: they name the code that logged the fault, and a long-lived system
/// accumulates a lot of them. The first two hundred are enough for the dropdown, and the cap
/// keeps the query from walking an unbounded list on every page load.
/// </remarks>
public sealed class GetErrorLogFacetsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetErrorLogFacetsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<ErrorLogFacetsResponse> HandleAsync(
        GetErrorLogFacetsQuery query,
        CancellationToken ct = default)
    {
        var levels = await _context.ErrorLogs
            .AsNoTracking()
            .Select(e => e.Level)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var sources = await _context.ErrorLogs
            .AsNoTracking()
            .Where(e => e.Source != null)
            .Select(e => e.Source!)
            .Distinct()
            .OrderBy(x => x)
            .Take(200)
            .ToListAsync(ct);

        return new ErrorLogFacetsResponse(levels, sources);
    }
}
