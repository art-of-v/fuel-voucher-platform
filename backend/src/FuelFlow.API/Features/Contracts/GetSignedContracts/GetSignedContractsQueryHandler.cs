using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetSignedContracts;

public sealed class GetSignedContractsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetSignedContractsQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Every signature on the platform, newest first.
    /// </summary>
    /// <remarks>
    /// The contract and the customer are read as columns rather than as entity graphs: the
    /// screen shows a name, a provider and a company, and the graph would carry the customer's
    /// phone, the company's EDRPOU and address and the whole contract row to fill none of them.
    /// </remarks>
    public async Task<List<AdminSignedContractDto>> HandleAsync(GetSignedContractsQuery query, CancellationToken ct = default)
    {
        var rows = await _context.UserContracts
            .AsNoTracking()
            .Select(uc => new
            {
                uc.Id,
                uc.ContractId,
                uc.UserId,
                uc.SignatureData,
                uc.SignedAtUtc,
                uc.TermsVersion,
                BrandId = uc.Contract != null ? (Guid?)uc.Contract.BrandId : null,
                BrandName = uc.Contract != null && uc.Contract.Brand != null ? uc.Contract.Brand.Name : null,
                CompanyId = uc.Contract != null ? (Guid?)uc.Contract.LegalEntityId : null,
                CompanyName = uc.Contract != null && uc.Contract.Entity != null ? uc.Contract.Entity.Name : null,
                ExpiresAtUtc = uc.Contract != null ? (DateTime?)uc.Contract.ExpiresAtUtc : null,
                FirstName = uc.User != null ? uc.User.FirstName : null,
                LastName = uc.User != null ? uc.User.LastName : null,
                PhoneNumber = uc.User != null ? uc.User.PhoneNumber : null
            })
            .OrderByDescending(uc => uc.SignedAtUtc)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        return rows.Select(r => new AdminSignedContractDto
        {
            Id = r.Id,
            ContractId = r.ContractId,
            BrandId = r.BrandId,
            BrandName = r.BrandName,
            CompanyId = r.CompanyId,
            CompanyName = r.CompanyName,
            UserId = r.UserId,
            CustomerName = CustomerDisplayName.Of(r.FirstName, r.LastName, r.PhoneNumber),
            SignedAtUtc = r.SignedAtUtc,
            ExpiresAtUtc = r.ExpiresAtUtc,
            Expired = r.ExpiresAtUtc != null && r.ExpiresAtUtc <= now,
            TermsVersion = r.TermsVersion,
            SignatureData = r.SignatureData
        }).ToList();
    }
}
