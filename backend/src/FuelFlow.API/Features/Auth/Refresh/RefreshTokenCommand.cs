using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken);

public sealed record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn
);

public sealed class RefreshTokenCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtTokenService _tokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        ApplicationDbContext context,
        IJwtTokenService tokenService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<RefreshTokenResponse> HandleAsync(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var token = command.RefreshToken.Trim();

        // Refresh tokens are stored hashed (SHA-256), so the lookup is by hash only.
        // There used to be a fallback that matched rt.Token against the raw value for rows
        // created before hashing existed. That fallback meant anyone with read access to the
        // refresh_tokens table - a stolen backup, a read replica, one SQL injection - held
        // directly replayable session credentials for every legacy row. This is a
        // pre-production deployment with no legacy rows to preserve, so the fallback is
        // removed: any such row now simply fails to refresh and the user logs in again.
        var tokenHash = SecretsHasher.Hash(token);
        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.Role)
            .Where(rt => rt.Token == tokenHash)
            .FirstOrDefaultAsync(cancellationToken);

        if (refreshToken == null || refreshToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            _logger.LogWarning("Invalid or expired refresh token");
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        // A ban already revokes every refresh token and bumps TokenVersion, so a banned user's
        // token would normally fail the reuse or version check anyway. This is defense in depth:
        // refuse outright rather than depend on the revoke having reached every row, and never
        // mint a fresh access token for a banned account.
        if (refreshToken.User.IsBanned)
        {
            _logger.LogWarning("Refresh refused for banned user {UserId}", refreshToken.UserId);
            throw new UnauthorizedAccessException("Account is banned");
        }

        if (refreshToken.IsRevoked)
        {
            var now = DateTime.UtcNow;

            // Lost-rotation recovery (#26). A token that was rotated but whose response never
            // reached the client - dropped connection, app suspended or killed mid-flight - is
            // replayed by the client on its next attempt. On the wire that looks exactly like a
            // stolen-token replay, but it has a narrow, self-limiting shape: the replay lands
            // within RefreshReuseGraceSeconds of the rotation AND the family still has exactly
            // one live successor (the client never advanced past it, because it never received
            // it). In that shape only, re-rotate from the replayed token instead of revoking the
            // family. Anything else - grace elapsed, a legacy family-less token, or zero/multiple
            // live tokens - falls through to strict reuse detection below. The window re-arms
            // after it elapses, so a genuine replay attack is still caught and the exposure is
            // bounded to the grace period.
            if (_jwtOptions.RefreshReuseGraceSeconds > 0
                && refreshToken.FamilyId != Guid.Empty
                && refreshToken.RevokedAtUtc is { } revokedAt
                && revokedAt >= now.AddSeconds(-_jwtOptions.RefreshReuseGraceSeconds))
            {
                var liveInFamily = await _context.RefreshTokens
                    .Where(rt => rt.UserId == refreshToken.UserId
                        && rt.FamilyId == refreshToken.FamilyId
                        && !rt.IsRevoked
                        && rt.ExpiresAtUtc > now)
                    .ToListAsync(cancellationToken);

                if (liveInFamily.Count == 1)
                {
                    _logger.LogWarning(
                        "Refresh token replayed within {GraceSeconds}s grace for user {UserId} (family {FamilyId}); "
                        + "treated as a benign lost-rotation and re-rotated rather than revoking the family",
                        _jwtOptions.RefreshReuseGraceSeconds,
                        refreshToken.UserId,
                        refreshToken.FamilyId);

                    // Revoke the orphaned successor the client never received and hand back a
                    // fresh one seeded from the replayed token's session identity.
                    return await RotateAsync(refreshToken, liveInFamily[0], now, cancellationToken);
                }
            }

            // Reuse detection: this token was already rotated, so someone is
            // replaying a stolen credential. Kill the entire family (all
            // sessions derived from the same login). Legacy tokens issued
            // before families existed carry an empty FamilyId - for those,
            // revoke every session of the user instead.
            var familyTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == refreshToken.UserId
                    && !rt.IsRevoked
                    && (refreshToken.FamilyId == Guid.Empty || rt.FamilyId == refreshToken.FamilyId))
                .ToListAsync(cancellationToken);

            foreach (var victim in familyTokens)
            {
                victim.IsRevoked = true;
                victim.RevokedAtUtc = now;
                _context.RefreshTokens.Update(victim);
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "SECURITY: refresh token reuse detected for user {UserId} (family {FamilyId}); revoked {Count} active token(s)",
                refreshToken.UserId,
                refreshToken.FamilyId,
                familyTokens.Count);

            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        // Inactive users (is_active=false) can refresh tokens - they have access to all
        // features except payments. Only payment endpoints enforce IsActive.
        var response = await RotateAsync(refreshToken, refreshToken, DateTime.UtcNow, cancellationToken);
        _logger.LogInformation("Refresh token rotated for user {UserId}", refreshToken.UserId);
        return response;
    }

    // Rotates the family forward: revokes revokeTarget (the token being consumed - the presented
    // token on a normal refresh, or the orphaned successor on a lost-rotation recovery) and mints
    // a fresh access + refresh pair seeded from identityToken's session identity (user, family,
    // device, and the role the session was pinned to at issue).
    private async Task<RefreshTokenResponse> RotateAsync(
        RefreshToken identityToken,
        RefreshToken revokeTarget,
        DateTime now,
        CancellationToken cancellationToken)
    {
        revokeTarget.IsRevoked = true;
        revokeTarget.RevokedAtUtc = now;
        _context.RefreshTokens.Update(revokeTarget);

        // Mint the access token with the role the SESSION was created with, NOT the user's
        // current role. If this user was promoted since login, refreshing must not silently
        // hand the existing session the new privileges (§16: new authorization only on a new
        // session). A demotion doesn't rely on this — it revokes the token outright. The
        // fallback to the live role covers only pre-migration rows with no snapshot.
        var sessionRole = identityToken.RoleNameAtIssue ?? identityToken.User.Role?.Name;
        var accessToken = _tokenService.GenerateAccessToken(identityToken.User.Id, identityToken.User.PhoneNumber, sessionRole, identityToken.User.FirstName, identityToken.User.LastName, identityToken.User.TokenVersion);
        var newRefreshTokenValue = _tokenService.GenerateRefreshToken();

        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = identityToken.UserId,
            FamilyId = identityToken.FamilyId,
            DeviceId = identityToken.DeviceId, // Preserve device linkage on rotation
            RoleNameAtIssue = identityToken.RoleNameAtIssue, // Carry the session's pinned role forward unchanged
            Token = SecretsHasher.Hash(newRefreshTokenValue),
            ExpiresAtUtc = now.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAtUtc = now,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenResponse(
            accessToken,
            newRefreshTokenValue,
            _jwtOptions.AccessTokenExpirationMinutes * 60
        );
    }
}
