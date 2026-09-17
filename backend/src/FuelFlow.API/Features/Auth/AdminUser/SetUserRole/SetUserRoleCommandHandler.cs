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

    public SetUserRoleCommandHandler(
        ApplicationDbContext context,
        ILogger<SetUserRoleCommandHandler> logger,
        ProviderEventService eventService)
    {
        _context = context;
        _logger = logger;
        _eventService = eventService;
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

        await _context.SaveChangesAsync(cancellationToken);

        await _eventService.RecordEventAsync(
            "User",
            target.Id.ToString(),
            "RoleChanged",
            JsonSerializer.Serialize(new { role = oldRoleName }),
            JsonSerializer.Serialize(new { role = command.RoleName }),
            command.ActingUserId,
            command.ActingUserName,
            "Role of " + target.PhoneNumber + " changed to " + command.RoleName,
            command.ActingUserId.ToString(),
            cancellationToken);

        _logger.LogInformation(
            "User {TargetId} role changed from {OldRole} to {NewRole} by {ActingUserId}",
            command.UserId, oldRoleName, command.RoleName, command.ActingUserId);

        return SetUserRoleResult.CreateSuccess();
    }
}
