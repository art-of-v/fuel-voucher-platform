using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
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

        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
                .ThenInclude(u => u.Role)
            .Where(rt => rt.Token == token && !rt.IsRevoked && rt.ExpiresAtUtc > DateTime.UtcNow)
            .FirstOrDefaultAsync(cancellationToken);

        if (refreshToken == null)
        {
            _logger.LogWarning("Invalid or expired refresh token");
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        refreshToken.IsRevoked = true;
        refreshToken.RevokedAtUtc = DateTime.UtcNow;

        var accessToken = _tokenService.GenerateAccessToken(refreshToken.User.Id, refreshToken.User.PhoneNumber, refreshToken.User.Role?.Name);
        var newRefreshTokenValue = _tokenService.GenerateRefreshToken();

        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = refreshToken.UserId,
            Token = newRefreshTokenValue,
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
