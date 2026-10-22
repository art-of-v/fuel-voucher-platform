using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FuelFlow.Features.Auth.AdminUser.SetUserActive;

public sealed class SetUserActiveCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SetUserActiveCommandHandler> _logger;
    private readonly AuditEventService _eventService;

    public SetUserActiveCommandHandler(
        ApplicationDbContext context,
        ILogger<SetUserActiveCommandHandler> logger,
        AuditEventService eventService)
    {
        _context = context;
        _logger = logger;
        _eventService = eventService;
    }

    public async Task<SetUserActiveResult> HandleAsync(
        SetUserActiveCommand command,
        CancellationToken cancellationToken)
    {
        // The default is TrackAll (DatabaseSetup), so this already tracks. Kept explicit because
        // so it must be tracked or SaveChanges would silently persist nothing.
        var target = await _context.Users
            .AsTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);

        if (target is null)
            return SetUserActiveResult.CreateNotFound("User not found");

        // Hierarchy check — enforced server-side, never trust the SPA dropdown.
        if (!RoleHierarchy.CanActivateDeactivate(command.ActingRole, target.Role?.Name))
        {
            _logger.LogWarning(
                "User {TargetId} activate/deactivate rejected: actor role {ActingRole} cannot manage target role {TargetRole}",
                command.UserId, command.ActingRole, target.Role?.Name);
            // The reason travels with the refusal: the log line above is the only place it used
            // to exist, and the operator had a toast that said nothing (planning #299).
            return SetUserActiveResult.CreateForbidden(
                RoleHierarchy.DescribeRefusal(command.ActingRole, target.Role?.Name));
        }

        if (target.IsActive == command.IsActive)
            return SetUserActiveResult.CreateSuccess(); // idempotent

        target.IsActive = command.IsActive;
        target.UpdatedAtUtc = SystemClock.UtcNow;
        // Activation changes purchase eligibility, not the validity of the session.

        _eventService.RecordEvent(
            "User",
            target.Id.ToString(),
            command.IsActive ? "UserActivated" : "UserDeactivated",
            null,
            JsonSerializer.Serialize(new
            {
                targetId = target.Id,
                isActive = target.IsActive,
                tokenVersion = target.TokenVersion,
                actorRole = command.ActingRole
            }),
            command.ActingUserId,
            command.ActingUserName,
            (command.IsActive ? "Activated user " : "Deactivated user ") + target.PhoneNumber,
            command.ActingUserId.ToString());

        // One save for the flag and the audit row: the row and the change it describes commit
        // together, or neither does (planning #533).
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {TargetId} {Action} by {ActingUserId} ({ActingRole})",
            command.UserId, command.IsActive ? "activated" : "deactivated", command.ActingUserId, command.ActingRole);

        return SetUserActiveResult.CreateSuccess();
    }
}
