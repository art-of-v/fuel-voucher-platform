using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.GetMyInvitations;

public sealed record GetMyInvitationsQuery(Guid WorkerUserId);

public sealed record MyCompanyInvitationDto(
    Guid Id,
    Guid LegalEntityId,
    string LegalEntityName,
    Guid OwnerUserId,
    string OwnerPhoneNumber,
    string? OwnerFirstName,
    string? OwnerLastName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class GetMyInvitationsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetMyInvitationsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<MyCompanyInvitationDto>> HandleAsync(GetMyInvitationsQuery query, CancellationToken cancellationToken = default)
    {
        return await _context.CompanyInvitations
            .AsNoTracking()
            .Where(x => x.WorkerUserId == query.WorkerUserId && x.Status == InvitationStatus.Pending)
            .Join(
                _context.LegalEntities.AsNoTracking(),
                invitation => invitation.LegalEntityId,
                legalEntity => legalEntity.Id,
                (invitation, legalEntity) => new { invitation, legalEntity })
            .Join(
                _context.Users.AsNoTracking(),
                combined => combined.invitation.OwnerUserId,
                owner => owner.Id,
                (combined, owner) => new
                {
                    combined.invitation.Id,
                    combined.invitation.LegalEntityId,
                    LegalEntityName = combined.legalEntity.Name,
                    combined.invitation.OwnerUserId,
                    owner.PhoneNumber,
                    owner.FirstName,
                    owner.LastName,
                    combined.invitation.CreatedAtUtc,
                    combined.invitation.UpdatedAtUtc
                })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MyCompanyInvitationDto(
                x.Id,
                x.LegalEntityId,
                x.LegalEntityName,
                x.OwnerUserId,
                x.PhoneNumber,
                x.FirstName,
                x.LastName,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
