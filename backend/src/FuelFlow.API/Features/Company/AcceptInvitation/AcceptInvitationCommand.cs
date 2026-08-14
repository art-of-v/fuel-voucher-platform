using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.AcceptInvitation;

public sealed record AcceptInvitationCommand(Guid InvitationId, Guid WorkerUserId);

public sealed record AcceptInvitationResult(string Status, Guid? MemberId = null, string? ErrorMessage = null);

public sealed class AcceptInvitationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public AcceptInvitationCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AcceptInvitationResult> HandleAsync(AcceptInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var invitation = await _context.CompanyInvitations
            .FirstOrDefaultAsync(x => x.Id == command.InvitationId, cancellationToken);

        if (invitation is null || invitation.WorkerUserId != command.WorkerUserId)
        {
            return new AcceptInvitationResult("NotFound", ErrorMessage: "Invitation not found.");
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return new AcceptInvitationResult("InvalidStatus", ErrorMessage: "Only pending invitations can be accepted.");
        }

        var alreadyMember = await _context.CompanyMembers
            .AsNoTracking()
            .AnyAsync(x => x.WorkerUserId == command.WorkerUserId, cancellationToken);

        if (alreadyMember)
        {
            return new AcceptInvitationResult("AlreadyMember", ErrorMessage: "Worker is already a member of another company.");
        }

        var member = new CompanyMember
        {
            Id = Guid.NewGuid(),
            LegalEntityId = invitation.LegalEntityId,
            WorkerUserId = command.WorkerUserId,
            JoinedAtUtc = DateTime.UtcNow
        };

        invitation.Status = InvitationStatus.Accepted;
        invitation.UpdatedAtUtc = DateTime.UtcNow;

        _context.CompanyMembers.Add(member);
        _context.CompanyInvitations.Update(invitation);
        await _context.SaveChangesAsync(cancellationToken);

        return new AcceptInvitationResult("Success", member.Id);
    }
}
