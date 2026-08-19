using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.CancelInvitation;

public sealed record CancelInvitationCommand(Guid InvitationId, Guid OwnerUserId);

public sealed record CancelInvitationResult(string Status, string? ErrorMessage = null);

public sealed class CancelInvitationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public CancelInvitationCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CancelInvitationResult> HandleAsync(CancelInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var invitation = await _context.CompanyInvitations
            .FirstOrDefaultAsync(x => x.Id == command.InvitationId && x.OwnerUserId == command.OwnerUserId, cancellationToken);

        if (invitation is null)
        {
            return new CancelInvitationResult("NotFound", "Invitation not found.");
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return new CancelInvitationResult("InvalidStatus", "Only pending invitations can be cancelled.");
        }

        invitation.Status = InvitationStatus.Cancelled;
        invitation.UpdatedAtUtc = DateTime.UtcNow;

        _context.CompanyInvitations.Update(invitation);
        await _context.SaveChangesAsync(cancellationToken);

        return new CancelInvitationResult("Success");
    }
}
