using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Auth.Verify;

public sealed record VerifyCodeCommand(string PhoneNumber, string Code);

public sealed record VerifyCodeResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn
);

public sealed class VerifyCodeCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtTokenService _tokenService;
    private readonly IPhoneNumberService _phoneNumberService;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<VerifyCodeCommandHandler> _logger;

    public VerifyCodeCommandHandler(
        ApplicationDbContext context,
        IJwtTokenService tokenService,
        IPhoneNumberService phoneNumberService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<VerifyCodeCommandHandler> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _phoneNumberService = phoneNumberService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<VerifyCodeResponse> HandleAsync(VerifyCodeCommand command, CancellationToken cancellationToken)
    {
        var phoneNumber = _phoneNumberService.Normalize(command.PhoneNumber);
        var code = command.Code.Trim();

        var verificationCode = await _context.VerificationCodes
            .Where(v => v.PhoneNumber == phoneNumber && v.Code == code && !v.IsUsed && v.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (verificationCode == null)
        {
            _logger.LogWarning("Invalid or expired verification code for {PhoneNumber}", phoneNumber);
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        verificationCode.IsUsed = true;
        verificationCode.UsedAtUtc = DateTime.UtcNow;

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);

        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.Users.Add(user);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;

        var accessToken = _tokenService.GenerateAccessToken(user.Id, user.PhoneNumber, user.Role?.Name);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenValue,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} authenticated successfully", user.Id);

        return new VerifyCodeResponse(
            accessToken,
            refreshTokenValue,
            _jwtOptions.AccessTokenExpirationMinutes * 60
        );
    }
}
