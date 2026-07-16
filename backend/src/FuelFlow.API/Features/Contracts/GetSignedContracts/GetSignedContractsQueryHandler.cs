using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetSignedContracts;

public sealed class GetSignedContractsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetSignedContractsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<UserContract>> HandleAsync(GetSignedContractsQuery query, CancellationToken ct = default) =>
        await _context.UserContracts.AsNoTracking()
            .Include(uc => uc.User).Include(uc => uc.Contract)
            .OrderByDescending(uc => uc.SignedAtUtc).ToListAsync(ct);
}
