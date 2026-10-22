using FuelFlow.Features.Auth.AdminUser;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Auth.DeleteUser;

public sealed record DeleteUserCommand(
    Guid UserId,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null,
    string? ActingRole = null);

public sealed class DeleteUserCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DeleteUserCommandHandler> _logger;
    private readonly AuditEventService _eventService;

    public DeleteUserCommandHandler(
        ApplicationDbContext context,
        ILogger<DeleteUserCommandHandler> logger,
        AuditEventService eventService)
    {
        _context = context;
        _logger = logger;
        _eventService = eventService;
    }

    public async Task<DeleteUserResult> HandleAsync(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null || user.IsDeleted)
        {
            return new DeleteUserResult { Success = false, Error = "User not found" };
        }

        // The hierarchy, like every other staff action. It was missing here and nowhere else:
        // the endpoint was gated on the AdminOrOwner policy, so an Admin could delete another
        // Admin and a Manager could not delete anything but could try.
        //
        // A user deleting their OWN account is not a staff action and is not the hierarchy's
        // business: the account is theirs to close.
        var isSelf = command.ActingAdminUserId == user.Id;

        if (!isSelf && !RoleHierarchy.CanDelete(command.ActingRole, user.Role?.Name))
        {
            _logger.LogWarning(
                "Delete of {TargetId} rejected: actor role {ActingRole} cannot delete target role {TargetRole}",
                command.UserId, command.ActingRole, user.Role?.Name);
            return new DeleteUserResult
            {
                Forbidden = true,
                Error = command.ActingRole is null
                    ? "Only staff can delete an account."
                    : $"A {command.ActingRole} cannot delete a {user.Role?.Name ?? "roleless"} account."
            };
        }

        // Never delete the last active ProductOwner — that would leave the platform with nobody
        // able to manage staff. The two neighbouring paths say the same thing about demoting and
        // banning the last one; deleting them was the way around both.
        if (AuthorizationHelpers.RoleEquals(user.Role?.Name, SeedRoles.ProductOwnerName))
        {
            var otherOwners = await _context.Users
                .AsNoTracking()
                .CountAsync(u => u.Id != user.Id && !u.IsDeleted && !u.IsBanned && u.IsActive
                                 && u.RoleId == SeedRoles.ProductOwnerId, cancellationToken);

            if (otherOwners == 0)
            {
                return new DeleteUserResult
                {
                    Forbidden = true,
                    Error = "Cannot delete the last active ProductOwner — assign another first."
                };
            }
        }

        user.IsDeleted = true;
        user.IsActive = false;
        user.TokenVersion++;
        user.UpdatedAtUtc = SystemClock.UtcNow;
        _context.Users.Update(user);

        var devices = await _context.Devices
            .Where(d => d.UserId == command.UserId && d.Status == DeviceStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var device in devices)
        {
            device.Status = DeviceStatus.Revoked;
            _context.Devices.Update(device);
        }

        var refreshTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == command.UserId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.IsRevoked = true;
            refreshToken.RevokedAtUtc = SystemClock.UtcNow;
            _context.RefreshTokens.Update(refreshToken);
        }

        var adminId = command.ActingAdminUserId ?? user.Id;
        _eventService.RecordEvent(
            "User",
            user.Id.ToString(),
            "UserDeleted",
            JsonSerializer.Serialize(new { user.Id, user.PhoneNumber }),
            "{}",
            adminId,
            command.ActingAdminName,
            $"Deleted user {user.PhoneNumber}",
            adminId.ToString());

        // One save for the deletion, the revoked sessions and the audit row: the row and the change
        // it describes commit together, or neither does (planning #533).
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} soft-deleted (is_active=false, token_version={Version}, devices/refresh tokens revoked)",
            user.Id, user.TokenVersion);

        return new DeleteUserResult { Success = true };
    }
}

public sealed class DeleteUserResult
{
    public bool Success { get; set; }

    /// <summary>Refused, with the reason - an empty 403 told the operator nothing (planning #455).</summary>
    public bool Forbidden { get; set; }

    public string? Error { get; set; }
}
