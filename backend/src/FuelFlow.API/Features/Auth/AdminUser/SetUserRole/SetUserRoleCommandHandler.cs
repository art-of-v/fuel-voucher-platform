using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FuelFlow.Features.Auth.AdminUser.SetUserRole;

public sealed class SetUserRoleCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SetUserRoleCommandHandler> _logger;
    private readonly ProviderEventService _eventService;
    private readonly LogoutEverywhereCommandHandler _logoutEverywhere;

    public SetUserRoleCommandHandler(
        ApplicationDbContext context,
        ILogger<SetUserRoleCommandHandler> logger,
        ProviderEventService eventService,
        LogoutEverywhereCommandHandler logoutEverywhere)
    {
        _context = context;
        _logger = logger;
        _eventService = eventService;
        _logoutEverywhere = logoutEverywhere;
    }

    public async Task<SetUserRoleResult> HandleAsync(
        SetUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        var target = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);

        if (target is null)
            return SetUserRoleResult.CreateNotFound("User not found");

        // Cannot change your own role.
        if (target.Id == command.ActingUserId)
            return SetUserRoleResult.CreateForbidden();

        // Hierarchy check — enforced server-side.
        if (!RoleHierarchy.CanAssignRole(command.ActingRole, target.Role?.Name, command.RoleName))
        {
            _logger.LogWarning(
                "Role change for {TargetId} rejected: actor role {ActingRole} cannot manage target role {TargetRole}",
                command.UserId, command.ActingRole, command.RoleName);
            return SetUserRoleResult.CreateForbidden();
        }

        // Cannot demote the last active ProductOwner.
        if (AuthorizationHelpers.RoleEquals(target.Role?.Name, "ProductOwner") &&
            !AuthorizationHelpers.RoleEquals(command.RoleName, "ProductOwner"))
        {
            var otherOwners = await _context.Users
                .AsNoTracking()
                .CountAsync(u => u.Id != target.Id && !u.IsDeleted && u.IsActive &&
                                 u.RoleId == SeedRoles.ProductOwnerId, cancellationToken);

            if (otherOwners == 0)
                return SetUserRoleResult.Failure("Cannot demote the last active ProductOwner — assign another first.");
        }

        var oldRoleName = target.Role?.Name;
        var targetRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == command.RoleName, cancellationToken);
        if (targetRole is null)
            return SetUserRoleResult.Failure($"Unknown role: {command.RoleName}");

        target.RoleId = targetRole.Id;
        target.Role = targetRole;
        target.UpdatedAtUtc = DateTime.UtcNow;

        // A demotion (new role ranks lower than the old one) strips privileges the user's live
        // sessions still carry, so §16 requires revoking every session immediately — mobile and
        // admin alike. This covers both losing Staff status entirely (Admin/Manager → User) and
        // stepping down within Staff (Admin → Manager). A PROMOTION deliberately does NOT revoke:
        // the existing session stays at its old authorization until the user starts a new one
        // (the RoleNameAtIssue snapshot keeps refresh from silently upgrading it).
        var isDemotion = SeedRoles.LevelFor(command.RoleName) < SeedRoles.LevelFor(oldRoleName);

        await _context.SaveChangesAsync(cancellationToken);

        if (isDemotion)
        {
            // Bumps TokenVersion (kills all access tokens within one request) and revokes every
            // refresh token + active device, on the same DbContext.
            await _logoutEverywhere.HandleAsync(new LogoutEverywhereCommand(target.Id), cancellationToken);

            _logger.LogWarning(
                "User {TargetId} demoted from {OldRole} to {NewRole} by {ActingUserId}; all sessions revoked",
                command.UserId, oldRoleName, command.RoleName, command.ActingUserId);
        }

        await _eventService.RecordEventAsync(
            "User",
            target.Id.ToString(),
            "RoleChanged",
            JsonSerializer.Serialize(new { role = oldRoleName }),
            JsonSerializer.Serialize(new { role = command.RoleName, sessionsRevoked = isDemotion }),
            command.ActingUserId,
            command.ActingUserName,
            "Role of " + target.PhoneNumber + " changed to " + command.RoleName,
            command.ActingUserId.ToString(),
            cancellationToken);

        if (!isDemotion)
        {
            _logger.LogInformation(
                "User {TargetId} role changed from {OldRole} to {NewRole} by {ActingUserId}",
                command.UserId, oldRoleName, command.RoleName, command.ActingUserId);
        }

        return SetUserRoleResult.CreateSuccess();
    }
}
