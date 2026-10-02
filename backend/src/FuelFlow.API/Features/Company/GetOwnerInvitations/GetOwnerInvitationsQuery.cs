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

        // Order over the source entity column and keep the complex DTO constructor (with the
        // converted-enum ToString) in a terminal Select — EF cannot translate OrderBy over a
        // property of an already-projected record, which otherwise throws at query time on
        // Npgsql (InMemory client-evaluates it, hiding the failure). Mirrors GetMembersQuery.
        return await _context.CompanyInvitations
            .AsNoTracking()
            .Where(x => x.OwnerUserId == query.OwnerUserId)
            .Where(x => scopedLegalEntityId == null || x.LegalEntityId == scopedLegalEntityId)
            .Join(
                _context.Users.AsNoTracking(),
                invitation => invitation.WorkerUserId,
                user => user.Id,
                (invitation, user) => new { invitation, user })
            .OrderByDescending(x => x.invitation.CreatedAtUtc)
            .Select(x => new CompanyInvitationDto(
                x.invitation.Id,
                x.invitation.LegalEntityId,
                x.invitation.OwnerUserId,
                x.invitation.WorkerUserId,
                x.invitation.WorkerPhoneNumber,
                x.user.FirstName,
                x.user.LastName,
                x.invitation.Status.ToString(),
                x.invitation.CreatedAtUtc,
                x.invitation.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
