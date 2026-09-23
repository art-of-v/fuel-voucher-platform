using FuelFlow.Persistence;
using FuelFlow.Features.Auth.SharedModels;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.Logout;

public sealed record LogoutDeviceCommand(string DeviceId, Guid UserId);
public sealed record LogoutEverywhereCommand(Guid UserId);

public sealed class LogoutDeviceCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<LogoutDeviceCommandHandler> _logger;

    public LogoutDeviceCommandHandler(
        ApplicationDbContext context,
        ILogger<LogoutDeviceCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task HandleAsync(LogoutDeviceCommand command, CancellationToken cancellationToken)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.DeviceId == command.DeviceId && d.UserId == command.UserId, cancellationToken);

        if (device is not null)
        {
            device.Status = DeviceStatus.Revoked;
            _context.Devices.Update(device);
        }

        // Revoke only refresh tokens belonging to THIS device.
        // Legacy tokens with null DeviceId are left alone (they can't be linked to a specific device).
        // This allows other devices to stay logged in.
        var refreshTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == command.UserId
                        && rt.DeviceId == command.DeviceId
                        && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.IsRevoked = true;
            refreshToken.RevokedAtUtc = now;
            _context.RefreshTokens.Update(refreshToken);
        }

        // Do NOT bump TokenVersion — that would invalidate access tokens on ALL devices.
        // Only the specific device's session is ended.

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Device {DeviceId} revoked for user {UserId}; {Count} refresh token(s) revoked (device-specific)",
            command.DeviceId, command.UserId, refreshTokens.Count);
    }
}

public sealed class LogoutEverywhereCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<LogoutEverywhereCommandHandler> _logger;

    public LogoutEverywhereCommandHandler(
        ApplicationDbContext context,
        ILogger<LogoutEverywhereCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task HandleAsync(LogoutEverywhereCommand command, CancellationToken cancellationToken)
    {
        // Revoke ALL refresh tokens for this user
        var refreshTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == command.UserId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.IsRevoked = true;
            refreshToken.RevokedAtUtc = now;
            _context.RefreshTokens.Update(refreshToken);
        }

        // Also revoke all devices for this user
        var devices = await _context.Devices
            .Where(d => d.UserId == command.UserId && d.Status == DeviceStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var device in devices)
        {
            device.Status = DeviceStatus.Revoked;
            _context.Devices.Update(device);
        }

        // Bump TokenVersion to invalidate all access tokens immediately.
        // AsTracking is REQUIRED here: callers such as SetUserRoleCommandHandler already track
        // this user (loaded .AsTracking().Include(Role) to flip the role). The context's global
        // NoTracking default would otherwise return a SECOND instance with the same key, and the
        // Update() below would throw an EF identity-map conflict (InvalidOperationException,
        // surfaced as HTTP 400). A tracking query returns the already-tracked instance when one
        // exists and tracks a fresh one otherwise, so this is correct whether or not the caller
        // pre-loaded the user.
        var user = await _context.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is not null)
        {
            user.TokenVersion++;
            user.UpdatedAtUtc = now;
            // No .Update(): the entity is tracked, so the change tracker emits a targeted UPDATE
            // for just these two columns. Update() would mark the whole graph (including any
            // Included Role) Modified and force spurious writes.
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} logged out everywhere; {Count} refresh token(s) revoked, {DeviceCount} device(s) revoked, token version bumped",
            command.UserId, refreshTokens.Count, devices.Count);
    }
}
