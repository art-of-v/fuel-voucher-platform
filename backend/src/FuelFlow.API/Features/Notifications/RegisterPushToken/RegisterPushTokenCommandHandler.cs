using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Notifications.RegisterPushToken;

public sealed class RegisterPushTokenCommandHandler
{
    private readonly ApplicationDbContext _context;

    public RegisterPushTokenCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RegisterPushTokenResponse> HandleAsync(
        RegisterPushTokenCommand command,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Tokens are globally unique. Look up by token so a device that re-logs-in as a
        // different user re-points the same token at the current user instead of duplicating
        // it (and so pushes never keep going to the previous owner of a shared device).
        var existing = await _context.PushTokens
            .FirstOrDefaultAsync(t => t.Token == command.Token, cancellationToken);

        if (existing == null)
        {
            _context.PushTokens.Add(new UserPushToken
            {
                Id = Guid.NewGuid(),
                UserId = command.UserId,
                Token = command.Token,
                Platform = command.Platform,
                DeviceId = command.DeviceId,
                IsActive = true,
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
            existing.UpdatedAtUtc = now;
            existing.LastSeenAtUtc = now;
            // Global NoTracking: the fetched row is detached, so re-attach as Modified
            // (mirrors MarkNotificationReadCommandHandler) or SaveChanges silently no-ops.
            _context.PushTokens.Update(existing);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new RegisterPushTokenResponse(true);
    }
}
