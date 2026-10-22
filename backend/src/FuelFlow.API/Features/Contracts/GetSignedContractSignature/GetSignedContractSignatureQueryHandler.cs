using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetSignedContractSignature;

/// <summary>
/// Serves one customer's handwritten signature, and records who asked for it.
/// </summary>
/// <remarks>
/// <para>
/// The image used to travel in the list of every signature on the platform, on the staff read
/// surface - one call was a harvest of the whole customer base's handwriting, unpaginated, with
/// the phone number beside it. The image is now behind a capability of its own
/// (<c>AuthPolicies.SignaturePayload</c>) and is served one record at a time.
/// </para>
/// <para>
/// <b>The audit row is the point of the endpoint.</b> A capability says who may look; the row
/// says who did. A signature cannot be revoked and cannot be rotated, so the record of who
/// opened it is the only control that follows a leak - without it, "the Admin tier may see the
/// signatures" is an answer to a question nobody can ask afterwards.
/// </para>
/// </remarks>
public sealed class GetSignedContractSignatureQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _audit;

    public GetSignedContractSignatureQueryHandler(ApplicationDbContext context, AuditEventService audit)
    {
        _context = context;
        _audit = audit;
    }

    public async Task<AdminSignedContractSignatureDto?> HandleAsync(
        GetSignedContractSignatureQuery query,
        CancellationToken ct = default)
    {
        var row = await _context.UserContracts
            .AsNoTracking()
            .Where(uc => uc.Id == query.SignatureId)
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
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        var customerName = CustomerDisplayName.Of(row.FirstName, row.LastName, row.PhoneNumber);

        // Who opened the image. The row carries the signature's identity and the contract it is
        // on - never the image itself: the audit trail is read by a wider surface than the
        // capability, so a row that carried the bytes would hand them out again.
        _audit.RecordEvent(
            "ContractSignature",
            row.Id.ToString(),
            "SignatureViewed",
            null,
            JsonSerializer.Serialize(new
            {
                row.ContractId,
                row.BrandName,
                customerName,
                row.TermsVersion,
                row.SignedAtUtc
            }),
            query.ActingUserId,
            query.ActingUserName,
            $"Viewed the signature on contract {row.ContractId}",
            row.BrandName ?? "all");

        // One save for the audit row: the row and the fact it describes commit together, or
        // neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        var now = SystemClock.UtcNow;

        return new AdminSignedContractSignatureDto
        {
            Id = row.Id,
            ContractId = row.ContractId,
            BrandId = row.BrandId,
            BrandName = row.BrandName,
            CompanyId = row.CompanyId,
            CompanyName = row.CompanyName,
            UserId = row.UserId,
            CustomerName = customerName,
            SignedAtUtc = row.SignedAtUtc,
            ExpiresAtUtc = row.ExpiresAtUtc,
            Expired = row.ExpiresAtUtc != null && row.ExpiresAtUtc <= now,
            TermsVersion = row.TermsVersion,
            SignatureData = row.SignatureData
        };
    }
}
