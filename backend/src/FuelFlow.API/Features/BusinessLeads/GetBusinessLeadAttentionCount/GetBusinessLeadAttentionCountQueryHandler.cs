using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.BusinessLeads.GetBusinessLeadAttentionCount;

public sealed class GetBusinessLeadAttentionCountQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetBusinessLeadAttentionCountQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<BusinessLeadAttentionResponse> HandleAsync(
        GetBusinessLeadAttentionCountQuery query,
        CancellationToken ct = default) =>
        new(await _context.BusinessLeads.CountAsync(l => l.Status == BusinessLeadStatus.New, ct));
}
