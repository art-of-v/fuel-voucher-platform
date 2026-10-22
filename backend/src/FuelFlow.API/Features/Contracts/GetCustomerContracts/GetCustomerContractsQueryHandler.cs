using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetCustomerContracts;

/// <summary>
/// Lists the contracts the caller is offered to sign.
/// </summary>
public sealed class GetCustomerContractsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetCustomerContractsQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Every contract the caller holds, newest first, with the text of the terms it is against.
    /// </summary>
    /// <param name="query">Whose contracts to list.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<List<CustomerContractDto>> HandleAsync(
        GetCustomerContractsQuery query,
        CancellationToken cancellationToken = default)
    {
        var contracts = await _context.Contracts
            .AsNoTracking()
            .Include(c => c.Entity)
            .Include(c => c.Brand)
            .Where(c => c.UserId == query.UserId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // The text the customer is asked to read before signing. It lives once per provider and
        // the contract says which version it is against - so the text is read by that pair, and
        // a contract whose version has no text yet says so rather than showing an empty page.
        // The table holds one row per provider per version - four providers and a handful of
        // versions each - so it is read whole and matched here.
        var texts = (await _context.ContractVersions
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToDictionary(v => (v.BrandId, v.Version), v => v.Body);

        var now = SystemClock.UtcNow;

        var result = contracts.Select(c =>
        {
            texts.TryGetValue((c.BrandId, c.TermsVersion), out var body);
            return new CustomerContractDto
            {
                Id = c.Id,
                LegalEntityId = c.LegalEntityId,
                LegalEntityName = c.Entity?.Name,
                BrandId = c.BrandId,
                BrandName = c.Brand?.Name,
                CreatedAt = c.CreatedAtUtc,
                // The validity term, and whether it has run out. A contract list that does not
                // say when a contract ends cannot answer "which of these is about to expire".
                ExpiresAt = c.ExpiresAtUtc,
                Expired = c.ExpiresAtUtc <= now,
                // Which version of the provider's text this contract is against, and the text
                // itself. The mobile's "REVIEW THE TEXT" had nothing behind it before this.
                TermsVersion = c.TermsVersion,
                TermsText = body,
                HasText = body is not null
            };
        }).ToList();

        return result;
    }
}
