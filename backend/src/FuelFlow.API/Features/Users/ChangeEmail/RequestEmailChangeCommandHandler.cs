using System.Text.Json;
using FuelFlow.Features.Auth.EmailChange;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Notifications.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Users.ChangeEmail;

/// <summary>
/// Initiates a verified, step-up-guarded self-service email change. Requires a fresh OTP proving
/// control of the current account (see <see cref="RequestEmailChangeCommand"/>), then reuses the
/// shared <see cref="PendingEmailChange"/> double-opt-in mechanics: stage the pending address and
/// email a confirmation link to it, leaving the active email untouched until that link is opened.
/// </summary>
public sealed class RequestEmailChangeCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly OtpVerificationService _otp;
    private readonly IEmailSender _emailSender;
    private readonly ProviderEventService _eventService;
    private readonly ILogger<RequestEmailChangeCommandHandler> _logger;

    public RequestEmailChangeCommandHandler(
        ApplicationDbContext context,
        OtpVerificationService otp,
        IEmailSender emailSender,
        ProviderEventService eventService,
        ILogger<RequestEmailChangeCommandHandler> logger)
    {
        _context = context;
        _otp = otp;
        _emailSender = emailSender;
        _eventService = eventService;
        _logger = logger;
    }

    public async Task<RequestEmailChangeResponse> HandleAsync(
        RequestEmailChangeCommand command, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(command.UserId, out var userId))
            throw new ArgumentException("Invalid user ID");

        var newEmail = (command.NewEmail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newEmail) || !PendingEmailChange.IsValidEmail(newEmail))
            throw new ArgumentException("Invalid email address");

        // AsTracking is required: the API DbContext defaults to NoTracking, and PendingEmailChange.Begin
        // mutates this entity in place — without tracking the SaveChangesAsync below is a silent no-op
        // and the pending change is never staged. See memory: notracking-mutation-trap.
        var user = await _context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
            throw new InvalidOperationException("User not found");

        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("New email must differ from the current email");

        // Email delivery is what makes the change confirmable; without it we would stage a pending
        // change that could never complete. Fail loudly rather than silently no-op.
        if (!_emailSender.IsConfigured)
            throw new InvalidOperationException("Email delivery is not configured");

        // Step-up: require a fresh OTP proving control of the CURRENT account. The code was sent by
        // send-code to the current phone/email (never the new address), so an attacker holding only
        // the new mailbox cannot pass this. A wrong/absent/expired code is rejected with the same
        // generic 403 — no oracle about code validity. The code is consumed (single-use) whether or
        // not it matched, mirroring login. 403 (not 401) is deliberate: the session is valid, only
        // the step-up factor failed — a 401 here would trip the mobile client's refresh-then-logout.
        var stepUpOk = await _otp.TryConsumeAsync(user.PhoneNumber, command.Code, cancellationToken);
        if (!stepUpOk)
        {
            _logger.LogWarning("Email-change step-up failed for user {UserId}", user.Id);
            throw new StepUpChallengeFailedException();
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

        return new RequestEmailChangeResponse(EmailChangePending: true);
    }
}
