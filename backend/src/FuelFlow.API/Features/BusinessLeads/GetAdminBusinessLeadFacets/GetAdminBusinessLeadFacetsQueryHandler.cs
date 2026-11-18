using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.BusinessLeads.GetAdminBusinessLeadFacets;

public sealed class GetAdminBusinessLeadFacetsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminBusinessLeadFacetsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<BusinessLeadFacetsResponse> HandleAsync(
        GetAdminBusinessLeadFacetsQuery query,
        CancellationToken ct = default)
    {
        var storedStatuses = await _context.BusinessLeads
            .AsNoTracking()
            .Select(l => l.Status)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);
        var statuses = storedStatuses.Select(s => s.ToString()).ToList();

        var totals = await _context.BusinessLeads
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(l => l.Status == BusinessLeadStatus.New
                    || l.Status == BusinessLeadStatus.Contacted
                    || l.Status == BusinessLeadStatus.Qualified),
                New = g.Count(l => l.Status == BusinessLeadStatus.New)
            })
            .FirstOrDefaultAsync(ct);

        return new BusinessLeadFacetsResponse(
            statuses,
            totals?.Total ?? 0,
            totals?.Active ?? 0,
            totals?.New ?? 0);
    }
}
