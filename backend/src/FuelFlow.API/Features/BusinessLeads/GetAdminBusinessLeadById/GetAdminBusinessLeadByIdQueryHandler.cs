using FuelFlow.Features.BusinessLeads.GetAdminBusinessLeads;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.BusinessLeads.GetAdminBusinessLeadById;

public sealed class GetAdminBusinessLeadByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminBusinessLeadByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<BusinessLeadDetailResponse?> HandleAsync(
        GetAdminBusinessLeadByIdQuery query,
        CancellationToken ct = default)
    {
        var lead = await _context.BusinessLeads
            .AsNoTracking()
            .SingleOrDefaultAsync(l => l.Id == query.Id, ct);

        return lead is null
            ? null
            : new BusinessLeadDetailResponse(GetAdminBusinessLeadsQueryHandler.Project(lead));
    }
}
