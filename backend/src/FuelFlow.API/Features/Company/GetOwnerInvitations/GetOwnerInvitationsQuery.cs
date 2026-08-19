using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.GetOwnerInvitations;

public sealed record GetOwnerInvitationsQuery(Guid OwnerUserId);

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
        return await _context.CompanyInvitations
            .AsNoTracking()
            .Where(x => x.OwnerUserId == query.OwnerUserId)
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
