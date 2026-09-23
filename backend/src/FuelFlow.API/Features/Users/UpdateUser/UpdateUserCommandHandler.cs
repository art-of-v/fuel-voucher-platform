using System.Text.Json;
using FuelFlow.Features.Auth.EmailChange;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Notifications.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Users.UpdateUser;

public sealed class UpdateUserCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailSender _emailSender;
    private readonly ProviderEventService _eventService;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        ApplicationDbContext context,
        IEmailSender emailSender,
        ProviderEventService eventService,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _context = context;
        _emailSender = emailSender;
        _eventService = eventService;
        _logger = logger;
    }

    public async Task<UpdateUserResponse> HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(command.UserId, out var userId))
            throw new ArgumentException("Invalid user ID");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
            throw new InvalidOperationException("User not found");

        // Email is NOT written directly here anymore. A self-service email change must be verified:
        // if the request carries a new address, stage it as a pending change and email the
        // confirmation link to that address; the active email only changes once the link is opened
        // (GET /api/auth/email/confirm). This closes the "email written unverified" gap and matches
        // the admin path. An unchanged or blank email is a no-op (clearing is a separate action).
        var emailChangePending = await TryBeginEmailChangeAsync(user, command, cancellationToken);

        if (command.FirstName is not null)
            user.FirstName = command.FirstName;

        if (command.LastName is not null)
            user.LastName = command.LastName;

        if (command.Birthdate is not null)
            user.Birthdate = command.Birthdate;

        if (command.ProfileImageUrl is not null)
            user.ProfileImageUrl = command.ProfileImageUrl;

        user.UpdatedAtUtc = DateTime.UtcNow;
        _context.Users.Update(user);

        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateUserResponse(
            user.Id,
            user.PhoneNumber,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Birthdate,
            user.ProfileImageUrl,
            user.ReferralCode,
            user.BonusBalance,
            emailChangePending
        );
    }

    private async Task<bool> TryBeginEmailChangeAsync(
        SharedKernel.Domain.User user, UpdateUserCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
            return false;

        var newEmail = command.Email.Trim();
        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            return false; // no change

        if (!PendingEmailChange.IsValidEmail(newEmail))
            throw new ArgumentException("Invalid email address");

        // Without a configured sender the change could never be confirmed. Leave the email
        // unchanged rather than silently writing an unverified address (dev-only path).
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning("Self-service email change requested for {UserId} but email delivery is not configured; ignoring", user.Id);
            return false;
        }

        var token = PendingEmailChange.Begin(user, newEmail);
        // Persist the pending fields before sending so a send failure cannot leave a live link
        // pointing at an unstored token.
        await _context.SaveChangesAsync(cancellationToken);

        await _emailSender.SendAsync(
            newEmail,
            PendingEmailChange.BuildConfirmationEmail(PendingEmailChange.BuildConfirmUrl(command.ConfirmBaseUrl, token)),
            cancellationToken);

        await _eventService.RecordEventAsync(
            "User", user.Id.ToString(), "EmailChangeRequested",
            JsonSerializer.Serialize(new { email = user.Email }),
            JsonSerializer.Serialize(new { pendingEmail = newEmail }),
            user.Id, null,
            "Self-service email change requested for " + user.PhoneNumber,
            user.Id.ToString(), cancellationToken);

        return true;
    }
}
