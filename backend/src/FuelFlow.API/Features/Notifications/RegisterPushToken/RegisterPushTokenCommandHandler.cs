using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Notifications.RegisterPushToken;

public sealed class RegisterPushTokenCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RegisterPushTokenCommandHandler> _logger;

    public RegisterPushTokenCommandHandler(
        ApplicationDbContext context,
        ILogger<RegisterPushTokenCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<RegisterPushTokenResponse> HandleAsync(
        RegisterPushTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        var now = SystemClock.UtcNow;

        // Tokens are globally unique. Look up by token so a device that re-logs-in as a
        // different user re-points the same token at the current user instead of duplicating
        // it (and so pushes never keep going to the previous owner of a shared device).
        var existing = await _context.PushTokens
            .FirstOrDefaultAsync(t => t.Token == command.Token, cancellationToken);

        // The client re-registers its push token right after the logout: its access token is
        // still valid for a moment, so the registration lands after the parking and would
        // undo it. A signed-out device has to stay parked - otherwise it keeps receiving the
        // business notifications the parking was meant to stop. The device registers itself
        // as Active when it signs in again, and that registration brings the full audience back.
        var deviceIsSignedOut = await IsDeviceSignedOutAsync(command.DeviceId, cancellationToken);

        if (existing == null)
        {
            _context.PushTokens.Add(new UserPushToken
            {
                Id = Guid.CreateVersion7(),
                UserId = command.UserId,
                Token = command.Token,
                Platform = command.Platform,
                DeviceId = command.DeviceId,
                IsActive = true,
                AuthOtpOnly = deviceIsSignedOut,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LastSeenAtUtc = now,
            });
        }
        else
        {
            existing.UserId = command.UserId;
            existing.Platform = command.Platform;
            existing.DeviceId = command.DeviceId;
            existing.IsActive = true;
            if (deviceIsSignedOut)
            {
                // The device is signed out, so it stays parked: the sign-in code may still
                // come here, the business notifications may not.
                _logger.LogInformation(
                    "The push token of the signed-out device {DeviceId} stayed parked",
                    command.DeviceId);
            }
            else
            {
                // The device is signing in, so it is a full audience again: the business
                // notifications it was parked from (on logout) start coming back.
                existing.AuthOtpOnly = false;
            }
            existing.UpdatedAtUtc = now;
            existing.LastSeenAtUtc = now;
            // Global NoTracking: the fetched row is detached, so re-attach as Modified
            // (mirrors MarkNotificationReadCommandHandler) or SaveChanges silently no-ops.
            _context.PushTokens.Update(existing);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new RegisterPushTokenResponse(true);
    }

    /// <summary>
    /// Whether the device that owns this token has signed out.
    /// </summary>
    /// <remarks>
    /// The device row carries the sign-in state of the device: the logout flips it to Revoked,
    /// the device registration flips it back to Active. A device id with no row - a device the
    /// retention job has already removed - counts as signed in: there is no state to read.
    /// </remarks>
    private async Task<bool> IsDeviceSignedOutAsync(string? deviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        return await _context.Devices
            .AsNoTracking()
            .AnyAsync(
                d => d.DeviceId == deviceId && d.Status == DeviceStatus.Revoked,
                cancellationToken);
    }
}
