using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Notifications.ParkPushToken;

public sealed class ParkPushTokenCommandHandler
{
    private readonly ApplicationDbContext _context;

    public ParkPushTokenCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ParkPushTokenResponse> HandleAsync(
        ParkPushTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        var hasToken = !string.IsNullOrEmpty(command.Token);
        var hasDeviceId = !string.IsNullOrEmpty(command.DeviceId);

        // Nothing to match on: the client sent neither its token nor a device id. Don't park
        // every token the user owns - that would mute their other devices.
        if (!hasToken && !hasDeviceId)
            return new ParkPushTokenResponse(0);

        // Scoped to the caller's own active rows. A token/device belonging to another user is never
        // touched even if the value collides, and a token Expo already reported as dead stays dead -
        // a parked flag on it would say "sign-in code may go here" about a phone that is gone.
        var matches = await _context.PushTokens
            .Where(t => t.UserId == command.UserId
                        && t.IsActive
                        && ((hasToken && t.Token == command.Token)
                            || (hasDeviceId && t.DeviceId == command.DeviceId)))
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
            return new ParkPushTokenResponse(0);

        var now = SystemClock.UtcNow;
        foreach (var token in matches)
        {
            // The token stays ACTIVE - Expo still knows it - and only its audience narrows:
            // business notifications stop, the sign-in code keeps its way in.
            token.AuthOtpOnly = true;
            token.UpdatedAtUtc = now;
            // Global NoTracking: the fetched rows are detached, so re-attach as Modified or
            // SaveChanges silently no-ops (see RegisterPushTokenCommandHandler).
            _context.PushTokens.Update(token);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new ParkPushTokenResponse(matches.Count);
    }
}
