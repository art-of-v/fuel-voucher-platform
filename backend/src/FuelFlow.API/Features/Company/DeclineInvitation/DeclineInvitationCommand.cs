using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.DeclineInvitation;

public sealed record DeclineInvitationCommand(Guid InvitationId, Guid WorkerUserId);

public sealed record DeclineInvitationResult(string Status, string? ErrorMessage = null);

public sealed class DeclineInvitationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeclineInvitationCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DeclineInvitationResult> HandleAsync(DeclineInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var invitation = await _context.CompanyInvitations
            .FirstOrDefaultAsync(x => x.Id == command.InvitationId, cancellationToken);

        if (invitation is null || invitation.WorkerUserId != command.WorkerUserId)
        {
            return new DeclineInvitationResult("NotFound", "Invitation not found.");
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return new DeclineInvitationResult("InvalidStatus", "Only pending invitations can be declined.");
        }

        invitation.Status = InvitationStatus.Declined;
        invitation.UpdatedAtUtc = DateTime.UtcNow;

        _context.CompanyInvitations.Update(invitation);
        await _context.SaveChangesAsync(cancellationToken);

        return new DeclineInvitationResult("Success");
    }
}
