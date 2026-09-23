using System.Text.Json;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Auth.ConfirmEmailChange;

/// <summary>
/// Completes a verified email change: the recipient of the confirmation link presents the
/// one-time token, and if it matches a live pending change the pending address is promoted to the
/// account's active email. The old address is notified. Public (unauthenticated) - possession of
/// the emailed token is the proof - so it is looked up by token hash only, never by user id.
/// </summary>
public sealed class ConfirmEmailChangeCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailSender _emailSender;
    private readonly ProviderEventService _eventService;
    private readonly ILogger<ConfirmEmailChangeCommandHandler> _logger;

    public ConfirmEmailChangeCommandHandler(
        ApplicationDbContext context,
        IEmailSender emailSender,
        ProviderEventService eventService,
        ILogger<ConfirmEmailChangeCommandHandler> logger)
    {
        _context = context;
        _emailSender = emailSender;
        _eventService = eventService;
        _logger = logger;
    }

    public async Task<ConfirmEmailChangeResult> HandleAsync(ConfirmEmailChangeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return new ConfirmEmailChangeResult(ConfirmEmailChangeStatus.InvalidToken);

        var tokenHash = SecretsHasher.Hash(command.Token.Trim());

        var user = await _context.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.PendingEmailTokenHash == tokenHash && !u.IsDeleted, cancellationToken);

        if (user is null || string.IsNullOrEmpty(user.PendingEmail))
            return new ConfirmEmailChangeResult(ConfirmEmailChangeStatus.InvalidToken);

        if (user.PendingEmailExpiresAtUtc is null || user.PendingEmailExpiresAtUtc < DateTime.UtcNow)
        {
            // Expired: burn the pending change so a stale link can never be replayed later.
            user.PendingEmail = null;
            user.PendingEmailTokenHash = null;
            user.PendingEmailExpiresAtUtc = null;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return new ConfirmEmailChangeResult(ConfirmEmailChangeStatus.Expired);
        }

        var oldEmail = user.Email;
        var newEmail = user.PendingEmail;

        user.Email = newEmail;
        user.PendingEmail = null;
        user.PendingEmailTokenHash = null;
        user.PendingEmailExpiresAtUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        await _eventService.RecordEventAsync(
            "User", user.Id.ToString(), "EmailChanged",
            JsonSerializer.Serialize(new { email = oldEmail }),
            JsonSerializer.Serialize(new { email = newEmail }),
            user.Id, null,
            "Email confirmed for " + user.PhoneNumber,
            user.Id.ToString(), cancellationToken);

        // Best-effort notice to the previous address so a silent hijack of the recovery channel is
        // visible to the prior owner. Never fails the confirmation.
        if (!string.IsNullOrWhiteSpace(oldEmail) && _emailSender.IsConfigured)
        {
            try
            {
                await _emailSender.SendAsync(oldEmail!, BuildEmailChangedNotice(newEmail), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify previous email address for user {UserId}", user.Id);
            }
        }

        _logger.LogInformation("Email change confirmed for user {UserId}", user.Id);
        return new ConfirmEmailChangeResult(ConfirmEmailChangeStatus.Confirmed);
    }

    /// <summary>Branded "your email was changed" notice sent to the PREVIOUS address so a silent
    /// hijack of the recovery channel is visible to the prior owner. The plain-text alternative keeps
    /// the exact prior wording.</summary>
    internal static EmailMessage BuildEmailChangedNotice(string newEmail) =>
        BrandedEmailLayout.BuildMessage(
            "Your FuelFlow email address was changed",
            "The email address on your FuelFlow account was changed to " + newEmail +
            ".\n\nIf you did not expect this, contact support immediately.",
            new BrandedEmailLayout.Content
            {
                Preheader = "The email address on your FuelFlow account was changed.",
                Heading = "Your email address was changed",
                Paragraphs =
                [
                    "The email address on your FuelFlow account was changed to " + newEmail + ".",
                    "If you did not expect this, contact support immediately.",
                ],
                Footnote = "This notice was sent to your previous address so any unexpected change stays visible to you.",
            });
}
