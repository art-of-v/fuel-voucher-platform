using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.SendInvitation;

public sealed record SendInvitationCommand(Guid OwnerUserId, string WorkerPhoneNumber, Guid? LegalEntityId = null);

public sealed record SendInvitationResult(string Status, Guid? InvitationId = null, string? ErrorMessage = null);

public sealed class SendInvitationCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IPhoneNumberService _phoneNumberService;

    public SendInvitationCommandHandler(ApplicationDbContext context, IPhoneNumberService phoneNumberService)
    {
        _context = context;
        _phoneNumberService = phoneNumberService;
    }

    public async Task<SendInvitationResult> HandleAsync(SendInvitationCommand command, CancellationToken cancellationToken = default)
    {
        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new SendInvitationResult("CompanyNotOwned", ErrorMessage: "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new SendInvitationResult("OwnerCompanyNotFound", ErrorMessage: "Owner does not have a legal entity profile.");
        }

        string normalizedPhone;
        try
        {
            normalizedPhone = _phoneNumberService.Normalize(command.WorkerPhoneNumber);
        }
        catch (ArgumentException)
        {
            return new SendInvitationResult("InvalidPhone", ErrorMessage: "Worker phone number format is invalid.");
        }

        var worker = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PhoneNumber == normalizedPhone && !x.IsDeleted, cancellationToken);

        if (worker is null)
        {
            return new SendInvitationResult("WorkerNotFound", ErrorMessage: "Invited worker must already be registered.");
        }

        if (worker.Id == command.OwnerUserId)
        {
            return new SendInvitationResult("CannotInviteSelf", ErrorMessage: "Owner cannot invite themselves.");
        }

        // Scoped to THIS company, not to the worker globally - #150 W1. A person may work for N
        // companies, so "already in a company" was never the question; "already in this one" is.
        // The old check also refused to invite anyone who worked anywhere, which is the exact
        // restriction W1 removes.
        var alreadyMember = await _context.CompanyMembers
            .AsNoTracking()
            .AnyAsync(x => x.LegalEntityId == legalEntityId && x.WorkerUserId == worker.Id, cancellationToken);

        if (alreadyMember)
        {
            return new SendInvitationResult("WorkerAlreadyMember", ErrorMessage: "Worker is already a member of this company.");
        }

        var alreadyPending = await _context.CompanyInvitations
            .AsNoTracking()
            .AnyAsync(x =>
                x.LegalEntityId == legalEntityId &&
                x.WorkerUserId == worker.Id &&
                x.Status == InvitationStatus.Pending,
                cancellationToken);

        if (alreadyPending)
        {
            return new SendInvitationResult("AlreadyPending", ErrorMessage: "A pending invitation already exists for this worker.");
        }

        var invitation = new CompanyInvitation
        {
            Id = Guid.CreateVersion7(),
            LegalEntityId = legalEntityId,
            OwnerUserId = command.OwnerUserId,
            WorkerPhoneNumber = normalizedPhone,
            WorkerUserId = worker.Id,
            Status = InvitationStatus.Pending,
            CreatedAtUtc = SystemClock.UtcNow,
            UpdatedAtUtc = SystemClock.UtcNow
        };

        _context.CompanyInvitations.Add(invitation);
        await _context.SaveChangesAsync(cancellationToken);

        return new SendInvitationResult("Success", invitation.Id);
    }
}
