using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetCustomerSignedContracts;

/// <summary>
/// Lists the signatures the caller has made.
/// </summary>
public sealed class GetCustomerSignedContractsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetCustomerSignedContractsQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Every signature the caller has made, newest first.
    /// </summary>
    /// <param name="query">Whose signatures to list.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<List<CustomerSignatureDto>> HandleAsync(
        GetCustomerSignedContractsQuery query,
        CancellationToken cancellationToken = default)
    {
        var signed = await _context.UserContracts
            .AsNoTracking()
            .Include(uc => uc.Contract)
                .ThenInclude(c => c!.Entity)
            .Include(uc => uc.Contract)
                .ThenInclude(c => c!.Brand)
            .Where(uc => uc.UserId == query.UserId)
            .OrderByDescending(uc => uc.SignedAtUtc)
            .ToListAsync(cancellationToken);

        var now = SystemClock.UtcNow;

        var result = signed.Select(uc => new CustomerSignatureDto
        {
            Id = uc.Id,
            ContractId = uc.ContractId,
            LegalEntityName = uc.Contract?.Entity?.Name,
            // The brand is carried by id, not only by name: a client that has to decide
            // "is this brand already signed?" needs something it can compare, and two
            // brands may share a name. The name is the label; the id is the fact.
            BrandId = uc.Contract?.BrandId,
            BrandName = uc.Contract?.Brand?.Name,
            SignedAt = uc.SignedAtUtc,
            // A signature outlives the contract it is a signature of, so the term travels
            // with it - otherwise a signed-contract list cannot say which of them has run out.
            ExpiresAt = uc.Contract?.ExpiresAtUtc,
            Expired = uc.Contract?.ExpiresAtUtc <= now
        }).ToList();

        return result;
    }
}
