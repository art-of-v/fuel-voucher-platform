using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.GetOwnerInvitations;

public sealed record GetOwnerInvitationsQuery(Guid OwnerUserId, Guid? LegalEntityId = null);

public sealed record CompanyInvitationDto(
    Guid Id,
    Guid LegalEntityId,
    Guid OwnerUserId,
    Guid WorkerUserId,
    string WorkerPhoneNumber,
    string? WorkerFirstName,
    string? WorkerLastName,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class GetOwnerInvitationsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetOwnerInvitationsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CompanyInvitationDto>> HandleAsync(GetOwnerInvitationsQuery query, CancellationToken cancellationToken = default)
    {
        Guid? scopedLegalEntityId = null;
        if (query.LegalEntityId is not null)
        {
            var resolution = await _context.ResolveOwnedLegalEntityAsync(query.OwnerUserId, query.LegalEntityId, cancellationToken);
            if (resolution.LegalEntityId is not { } owned)
            {
                // An entity id that is not the caller's — nothing to show.
                return Array.Empty<CompanyInvitationDto>();
            }
            scopedLegalEntityId = owned;
        }

        return await _context.CompanyInvitations
            .AsNoTracking()
            .Where(x => x.OwnerUserId == query.OwnerUserId)
            .Where(x => scopedLegalEntityId == null || x.LegalEntityId == scopedLegalEntityId)
            .Join(
                _context.Users.AsNoTracking(),
                invitation => invitation.WorkerUserId,
                user => user.Id,
                (invitation, user) => new CompanyInvitationDto(
                    invitation.Id,
                    invitation.LegalEntityId,
                    invitation.OwnerUserId,
                    invitation.WorkerUserId,
                    invitation.WorkerPhoneNumber,
                    user.FirstName,
                    user.LastName,
                    invitation.Status.ToString(),
                    invitation.CreatedAtUtc,
                    invitation.UpdatedAtUtc))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
