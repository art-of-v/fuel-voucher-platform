using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Notifications.DeregisterPushToken;

public sealed class DeregisterPushTokenCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeregisterPushTokenCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DeregisterPushTokenResponse> HandleAsync(
        DeregisterPushTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        var hasToken = !string.IsNullOrEmpty(command.Token);
        var hasDeviceId = !string.IsNullOrEmpty(command.DeviceId);

        // Nothing to match on: the client sent neither its token nor a device id. Don't deactivate
        // every token the user owns — that would silently kill push on their other devices.
        if (!hasToken && !hasDeviceId)
            return new DeregisterPushTokenResponse(0);

        // Scoped to the caller's own active rows. A token/device belonging to another user is never
        // touched even if the value collides.
        var matches = await _context.PushTokens
            .Where(t => t.UserId == command.UserId
                        && t.IsActive
                        && ((hasToken && t.Token == command.Token)
                            || (hasDeviceId && t.DeviceId == command.DeviceId)))
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
            return new DeregisterPushTokenResponse(0);

        var now = DateTime.UtcNow;
        foreach (var token in matches)
        {
            token.IsActive = false;
            token.UpdatedAtUtc = now;
            // Global NoTracking: the fetched rows are detached, so re-attach as Modified or
            // SaveChanges silently no-ops (see RegisterPushTokenCommandHandler).
            _context.PushTokens.Update(token);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new DeregisterPushTokenResponse(matches.Count);
    }
}
