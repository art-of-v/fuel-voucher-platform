using System.Text.Json;
using FuelFlow.Features.Auth.EmailChange;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Auth.AdminUser.SetUserEmail;

public sealed class SetUserEmailCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailSender _emailSender;
    private readonly AuditEventService _eventService;
    private readonly ILogger<SetUserEmailCommandHandler> _logger;

    public SetUserEmailCommandHandler(
        ApplicationDbContext context,
        IEmailSender emailSender,
        AuditEventService eventService,
        ILogger<SetUserEmailCommandHandler> logger)
    {
        _context = context;
        _emailSender = emailSender;
        _eventService = eventService;
        _logger = logger;
    }

    public async Task<SetUserEmailResult> HandleAsync(SetUserEmailCommand command, CancellationToken cancellationToken)
    {
        // The default is TrackAll (DatabaseSetup), so this already tracks; kept explicit because
        // must be tracked or SaveChanges silently persists nothing.
        var target = await _context.Users
            .AsTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);

        if (target is null)
            return SetUserEmailResult.CreateNotFound("User not found");

        // Same authority as activate/deactivate: PO -> anyone below, Admin -> User/Manager,
        // Manager -> User. A ProductOwner target is only ever reachable by a ProductOwner, keeping
        // the PO untouchable by lower staff.
        if (!RoleHierarchy.CanActivateDeactivate(command.ActingRole, target.Role?.Name))
        {
            _logger.LogWarning(
                "Email change for {TargetId} rejected: actor role {ActingRole} cannot manage target role {TargetRole}",
                command.UserId, command.ActingRole, target.Role?.Name);
            return SetUserEmailResult.CreateForbidden(
                RoleHierarchy.DescribeRefusal(command.ActingRole, target.Role?.Name));
        }

        var oldEmail = target.Email;

        // Empty -> clear the address directly. No verification needed: staff simply fall back to
        // SMS for OTP, and there is nothing to prove control of.
        if (string.IsNullOrWhiteSpace(command.NewEmail))
        {
            target.Email = null;
            target.PendingEmail = null;
            target.PendingEmailTokenHash = null;
            target.PendingEmailExpiresAtUtc = null;
            target.UpdatedAtUtc = SystemClock.UtcNow;

            _eventService.RecordEvent(
                "User", target.Id.ToString(), "EmailCleared",
                JsonSerializer.Serialize(new { email = oldEmail }),
                JsonSerializer.Serialize(new { email = (string?)null }),
                command.ActingUserId, command.ActingName,
                "Email cleared for " + target.PhoneNumber,
                command.ActingUserId.ToString());

            // One save for the cleared address and the audit row: the row and the change it
            // describes commit together, or neither does (planning #533).
            await _context.SaveChangesAsync(cancellationToken);

            return SetUserEmailResult.CreateCleared();
        }

        var newEmail = command.NewEmail.Trim();
        if (!PendingEmailChange.IsValidEmail(newEmail))
            return SetUserEmailResult.Failure("Invalid email address");

        // A verified change is pointless if we cannot deliver the confirmation link.
        if (!_emailSender.IsConfigured)
            return SetUserEmailResult.Failure("Email delivery is not configured");

        // Stage as pending (active email untouched) and email the confirmation link to the new
        // address; the public confirm endpoint promotes it. Only the token hash is stored.
        var token = PendingEmailChange.Begin(target, newEmail);

        // Tracked before the save, so the pending change and the row that records it commit
        // together - a request that was made must never exist without its row (planning #533).
        _eventService.RecordEvent(
            "User", target.Id.ToString(), "EmailChangeRequested",
            JsonSerializer.Serialize(new { email = oldEmail }),
            JsonSerializer.Serialize(new { pendingEmail = newEmail }),
            command.ActingUserId, command.ActingName,
            "Email change requested for " + target.PhoneNumber,
            command.ActingUserId.ToString());

        await _context.SaveChangesAsync(cancellationToken);

        await _emailSender.SendAsync(
            newEmail,
            PendingEmailChange.BuildConfirmationEmail(PendingEmailChange.BuildConfirmUrl(command.ConfirmBaseUrl, token)),
            cancellationToken);

        _logger.LogInformation(
            "Email change requested for user {TargetId} by {ActingUserId}; confirmation sent",
            target.Id, command.ActingUserId);

        return SetUserEmailResult.CreatePending();
    }
}
