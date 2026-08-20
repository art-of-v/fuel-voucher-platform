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

        // Refresh tokens are stored hashed (SHA-256). Load by hash; fall back
        // to a raw-value lookup so rows created before hashing was introduced
        // keep working — they are replaced with a hash on their next rotation.
        var tokenHash = SecretsHasher.Hash(token);
        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.Role)
            .Where(rt => rt.Token == tokenHash)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _context.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.Role)
            .Where(rt => rt.Token == token)
            .FirstOrDefaultAsync(cancellationToken);

        if (refreshToken == null || refreshToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            _logger.LogWarning("Invalid or expired refresh token");
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        if (refreshToken.IsRevoked)
        {
            // Reuse detection: this token was already rotated, so someone is
            // replaying a stolen credential. Kill the entire family (all
            // sessions derived from the same login). Legacy tokens issued
            // before families existed carry an empty FamilyId - for those,
            // revoke every session of the user instead.
            var now = DateTime.UtcNow;
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

        if (!refreshToken.User.IsActive)
        {
            _logger.LogWarning("Refresh rejected for deactivated user {UserId}", refreshToken.UserId);
            throw new UnauthorizedAccessException("Account is deactivated");
        }

        refreshToken.IsRevoked = true;
        refreshToken.RevokedAtUtc = DateTime.UtcNow;
        _context.RefreshTokens.Update(refreshToken);

        var accessToken = _tokenService.GenerateAccessToken(refreshToken.User.Id, refreshToken.User.PhoneNumber, refreshToken.User.Role?.Name, refreshToken.User.FirstName, refreshToken.User.LastName, refreshToken.User.TokenVersion);
        var newRefreshTokenValue = _tokenService.GenerateRefreshToken();

        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = refreshToken.UserId,
            FamilyId = refreshToken.FamilyId,
            Token = SecretsHasher.Hash(newRefreshTokenValue),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Refresh token rotated for user {UserId}", refreshToken.UserId);

        return new RefreshTokenResponse(
            accessToken,
            newRefreshTokenValue,
            _jwtOptions.AccessTokenExpirationMinutes * 60
        );
    }
}
