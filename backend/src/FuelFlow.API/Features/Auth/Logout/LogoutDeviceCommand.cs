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

        // Revoking the device row alone was not a logout. It stopped this device from
        // signing purchases and from re-authenticating via challenge/verify, but it left
        // every issued credential live: the access token stayed valid to expiry, and the
        // refresh token stayed valid for RefreshTokenExpirationDays and rotated into a
        // fresh 7-day token on each use (RefreshTokenCommand.cs:114). Anyone who had
        // captured the refresh token therefore kept the account indefinitely, and could
        // re-enrol their own key over the revoked row to restore device-signed purchases.
        // Logging out has to end the session, so revoke the credentials too. RefreshToken
        // carries no device linkage, so this necessarily signs the user out on every
        // device; other devices recover silently via biometric challenge/verify, which is
        // the safer default. This mirrors DeleteUserCommandHandler.cs:40-63.
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

        // Invalidates already-issued access tokens via SessionValidationMiddleware:50.
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is not null)
        {
            user.TokenVersion++;
            user.UpdatedAtUtc = now;
            _context.Users.Update(user);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Device {DeviceId} revoked for user {UserId}; {Count} refresh token(s) revoked, token version bumped",
            command.DeviceId, command.UserId, refreshTokens.Count);
    }
}
