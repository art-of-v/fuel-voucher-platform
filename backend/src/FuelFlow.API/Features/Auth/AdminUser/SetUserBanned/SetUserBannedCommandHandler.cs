using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FuelFlow.Features.Auth.AdminUser.SetUserBanned;

public sealed class SetUserBannedCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SetUserBannedCommandHandler> _logger;
    private readonly ProviderEventService _eventService;

    public SetUserBannedCommandHandler(
        ApplicationDbContext context,
        ILogger<SetUserBannedCommandHandler> logger,
        ProviderEventService eventService)
    {
        _context = context;
        _logger = logger;
        _eventService = eventService;
    }

    public async Task<SetUserBannedResult> HandleAsync(
        SetUserBannedCommand command,
        CancellationToken cancellationToken)
    {
        // Global query default is NoTracking (DatabaseSetup). This entity is mutated below
        // (IsBanned, TokenVersion), so it must be tracked or SaveChanges would silently
        // persist nothing — leaving the user un-banned while their sessions are revoked.
        var target = await _context.Users
            .AsTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == command.UserId && !u.IsDeleted, cancellationToken);

        if (target is null)
            return SetUserBannedResult.CreateNotFound("User not found");

        // Banning yourself would revoke your own session and, for a lone ProductOwner,
        // lock the whole system out. Never allowed in either direction.
        if (target.Id == command.ActingUserId)
            return SetUserBannedResult.CreateForbidden();

        // Hierarchy check — enforced server-side, never trust the SPA. Ban authority mirrors
        // delete authority (ProductOwner → anyone, Admin → User/Manager): both are hard
        // revocations, unlike deactivate which a Manager may apply to a User.
        if (!RoleHierarchy.CanBan(command.ActingRole, target.Role?.Name))
        {
            _logger.LogWarning(
                "User {TargetId} ban/unban rejected: actor role {ActingRole} cannot manage target role {TargetRole}",
                command.UserId, command.ActingRole, target.Role?.Name);
            return SetUserBannedResult.CreateForbidden();
        }

        // Never ban the last active, non-banned ProductOwner — that would leave the system
        // with no one able to manage staff. (Self-ban is already blocked above, so the actor
        // banning a *different* PO can still strand the platform if it was the last one.)
        if (command.IsBanned
            && AuthorizationHelpers.RoleEquals(target.Role?.Name, SeedRoles.ProductOwnerName))
        {
            var otherOwners = await _context.Users
                .AsNoTracking()
                .CountAsync(u => u.Id != target.Id && !u.IsDeleted && !u.IsBanned && u.IsActive
                                 && u.RoleId == SeedRoles.ProductOwnerId, cancellationToken);

            if (otherOwners == 0)
                return SetUserBannedResult.Failure("Cannot ban the last active ProductOwner — assign another first.");
        }

        if (target.IsBanned == command.IsBanned)
            return SetUserBannedResult.CreateSuccess(); // idempotent

        var now = DateTime.UtcNow;
        target.IsBanned = command.IsBanned;
        target.UpdatedAtUtc = now;

        if (command.IsBanned)
        {
            // A ban is a hard revocation. Bumping TokenVersion invalidates every outstanding
            // access token immediately (SessionValidationMiddleware compares the signed claim
            // to this value on each request); revoking refresh tokens + devices stops the
            // holder from silently minting a new access token at /refresh.
            //
            // Because the refresh tokens are revoked here, a later UNBAN restores nothing: the
            // user must send-code/verify from scratch. That asymmetry is the whole point — an
            // unban is "may log in again", not "resume the session that was banned".
            target.TokenVersion++;

            var refreshTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == target.Id && !rt.IsRevoked)
                .ToListAsync(cancellationToken);
            foreach (var refreshToken in refreshTokens)
            {
                refreshToken.IsRevoked = true;
                refreshToken.RevokedAtUtc = now;
                _context.RefreshTokens.Update(refreshToken);
            }

            var devices = await _context.Devices
                .Where(d => d.UserId == target.Id && d.Status == DeviceStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var device in devices)
            {
                device.Status = DeviceStatus.Revoked;
                _context.Devices.Update(device);
            }

            _logger.LogWarning(
                "User {TargetId} banned by {ActingUserId} ({ActingRole}); token_version={Version}, {TokenCount} refresh token(s) and {DeviceCount} device(s) revoked",
                target.Id, command.ActingUserId, command.ActingRole, target.TokenVersion,
                refreshTokens.Count, devices.Count);
        }
        else
        {
            // Unban only lifts the flag. TokenVersion, refresh tokens and devices are left as
            // the ban revoked them, so no prior session comes back to life — the user
            // re-authenticates.
            _logger.LogInformation(
                "User {TargetId} unbanned by {ActingUserId} ({ActingRole}); prior sessions remain revoked",
                target.Id, command.ActingUserId, command.ActingRole);
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _eventService.RecordEventAsync(
            "User",
            target.Id.ToString(),
            command.IsBanned ? "UserBanned" : "UserUnbanned",
            null,
            JsonSerializer.Serialize(new
            {
                targetId = target.Id,
                isBanned = target.IsBanned,
                tokenVersion = target.TokenVersion,
                actorRole = command.ActingRole
            }),
            command.ActingUserId,
            command.ActingUserName,
            (command.IsBanned ? "Banned user " : "Unbanned user ") + target.PhoneNumber,
            command.ActingUserId.ToString(),
            cancellationToken);

        return SetUserBannedResult.CreateSuccess();
    }
}
