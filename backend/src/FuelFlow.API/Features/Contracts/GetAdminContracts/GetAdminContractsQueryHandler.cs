using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetAdminContracts;

public sealed class GetAdminContractsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminContractsQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Every contract on the platform, newest first.
    /// </summary>
    /// <remarks>
    /// The columns are chosen in the query rather than loaded as entities: the screen shows a
    /// provider, a company, a customer, a term and a signature state, and an entity graph would
    /// carry the customer's phone, the company's EDRPOU and address, and the provider's whole row
    /// to fill none of them.
    /// <para>
    /// The signatures are read in a second query and matched by contract, because a per-contract
    /// lookup would be one query per row - and the list is exactly the place where that hurts.
    /// </para>
    /// </remarks>
    public async Task<List<AdminContractDto>> HandleAsync(GetAdminContractsQuery query, CancellationToken ct = default)
    {
        var rows = await _context.Contracts
            .AsNoTracking()
            .Select(c => new
            {
                c.Id,
                c.BrandId,
                BrandName = c.Brand != null ? c.Brand.Name : null,
                c.LegalEntityId,
                CompanyName = c.Entity != null ? c.Entity.Name : null,
                c.UserId,
                FirstName = c.User != null ? c.User.FirstName : null,
                LastName = c.User != null ? c.User.LastName : null,
                PhoneNumber = c.User != null ? c.User.PhoneNumber : null,
                c.CreatedAtUtc,
                c.ExpiresAtUtc,
                c.TermsVersion
            })
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(ct);

        var contractIds = rows.Select(r => r.Id).ToList();

        // One signature per contract - the sign endpoint refuses a second one - so the latest
        // one is the only one there can be.
        var signatures = await _context.UserContracts
            .AsNoTracking()
            .Where(uc => contractIds.Contains(uc.ContractId))
            .GroupBy(uc => uc.ContractId)
            .Select(g => new { ContractId = g.Key, SignedAtUtc = g.Max(uc => uc.SignedAtUtc) })
            .ToListAsync(ct);

        var signedAtByContract = signatures.ToDictionary(s => s.ContractId, s => s.SignedAtUtc);
        var now = SystemClock.UtcNow;

        return rows.Select(r =>
        {
            var signed = signedAtByContract.TryGetValue(r.Id, out var signedAt) && signedAt != default;

            return new AdminContractDto
            {
                Id = r.Id,
                BrandId = r.BrandId,
                BrandName = r.BrandName,
                LegalEntityId = r.LegalEntityId,
                CompanyName = r.CompanyName,
                UserId = r.UserId,
                CustomerName = CustomerDisplayName.Of(r.FirstName, r.LastName, r.PhoneNumber),
                CreatedAtUtc = r.CreatedAtUtc,
                ExpiresAtUtc = r.ExpiresAtUtc,
                Expired = r.ExpiresAtUtc <= now,
                TermsVersion = r.TermsVersion,
                Signed = signed,
                SignedAtUtc = signed ? signedAt : null
            };
        }).ToList();
    }
}
