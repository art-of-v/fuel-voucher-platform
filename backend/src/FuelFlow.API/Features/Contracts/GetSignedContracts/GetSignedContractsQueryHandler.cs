using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetSignedContracts;

public sealed class GetSignedContractsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetSignedContractsQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// A page of the signatures on the platform, newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contract and the customer are read as columns rather than as entity graphs: the
    /// screen shows a name, a provider and a company, and the graph would carry the customer's
    /// phone, the company's EDRPOU and address and the whole contract row to fill none of them.
    /// </para>
    /// <para>
    /// <b>No signature image leaves here.</b> This used to answer with every signature on the
    /// platform at once, each row carrying the customer's handwritten signature in base64 - one
    /// call was a harvest of the whole customer base's handwriting, on the staff read surface. A
    /// signature cannot be revoked and cannot be rotated, so it is a capability of its own
    /// (<c>AuthPolicies.SignaturePayload</c>), the image is served per record
    /// (<c>GetSignedContractSignature</c>), and what is opened is recorded.
    /// </para>
    /// <para>
    /// The list is a PAGE for the same reason the payload was not: an endpoint that answers with
    /// the whole table is an endpoint that materialises the whole table.
    /// </para>
    /// </remarks>
    public async Task<PagedResult<AdminSignedContractDto>> HandleAsync(
        GetSignedContractsQuery query,
        CancellationToken ct = default)
    {
        var signatures = _context.UserContracts.AsNoTracking();

        // The count is taken from the same query the page is, so the total and the rows answer
        // the same question.
        var total = await signatures.CountAsync(ct);

        var limit = PageLimits.ClampPageSize(query.Limit);

        var rows = await signatures
            // The tie-break: many signatures share a timestamp to the second, and without a unique
            // second key the sort is unstable - a row can move between pages.
            .OrderByDescending(uc => uc.SignedAtUtc)
            .ThenBy(uc => uc.Id)
            .Skip(PageLimits.SkipFor(PageLimits.ClampPage(query.Page), limit))
            .Take(limit)
            .Select(uc => new
            {
                uc.Id,
                uc.ContractId,
                uc.UserId,
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
            .ToListAsync(ct);

        var now = SystemClock.UtcNow;

        return new PagedResult<AdminSignedContractDto>
        {
            Items = rows.Select(r => new AdminSignedContractDto
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
                TermsVersion = r.TermsVersion
            }).ToList(),
            TotalCount = total,
            Page = PageLimits.ClampPage(query.Page),
            PageSize = limit
        };
    }
}
