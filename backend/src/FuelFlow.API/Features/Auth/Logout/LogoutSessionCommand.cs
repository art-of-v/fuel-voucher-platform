using FuelFlow.SharedKernel.Security;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.Logout;

public sealed record LogoutSessionCommand(string RefreshToken);

/// <summary>
/// Ends a single browser/cookie session by revoking the one refresh token it
/// presents. Used by the admin panel, which is a cookie-session client and never
/// registers a device_id (device linking only happens in the mobile challenge
/// flow), so <see cref="LogoutDeviceCommandHandler"/> - which filters by DeviceId -
/// can never match its token. Other sessions (mobile, other admin browsers) are
/// left logged in, and TokenVersion is untouched.
/// </summary>
public sealed class LogoutSessionCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<LogoutSessionCommandHandler> _logger;

    public LogoutSessionCommandHandler(
        ApplicationDbContext context,
        ILogger<LogoutSessionCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task HandleAsync(LogoutSessionCommand command, CancellationToken cancellationToken)
    {
        // Refresh tokens are stored hashed (SHA-256), so match on the hash of the
        // presented value - same lookup as /api/auth/refresh.
        var tokenHash = SecretsHasher.Hash(command.RefreshToken.Trim());

        var refreshToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == tokenHash && !rt.IsRevoked, cancellationToken);

        // Unknown or already-revoked token: nothing to do. Logout stays idempotent
        // so a stale cookie never turns into an error.
        if (refreshToken is null)
            return;

        refreshToken.IsRevoked = true;
        refreshToken.RevokedAtUtc = DateTime.UtcNow;
        _context.RefreshTokens.Update(refreshToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Session logged out for user {UserId}; 1 refresh token revoked (session-specific)",
            refreshToken.UserId);
    }
}
