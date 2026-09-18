using FuelFlow.Persistence;
using FuelFlow.Features.Auth.SharedModels;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.Logout;

public sealed record LogoutDeviceCommand(string DeviceId, Guid UserId);

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
