using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

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
    private readonly ProviderEventService _eventService;

    public VerifyCodeCommandHandler(
        ApplicationDbContext context,
        IJwtTokenService tokenService,
        IPhoneNumberService phoneNumberService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<VerifyCodeCommandHandler> logger,
        ProviderEventService eventService)
    {
        _context = context;
        _tokenService = tokenService;
        _phoneNumberService = phoneNumberService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
        _eventService = eventService;
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
            await LogFailedAdminLoginAsync(phoneNumber, "Invalid or expired verification code", cancellationToken);
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        verificationCode.IsUsed = true;
        verificationCode.UsedAtUtc = DateTime.UtcNow;
        _context.VerificationCodes.Update(verificationCode);

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && !u.IsDeleted, cancellationToken);

        bool isNewUser = false;
        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.Users.Add(user);
            isNewUser = true;
        }
        else if (!user.IsActive)
        {
            _logger.LogWarning("Login rejected for deactivated user {UserId}", user.Id);
            await LogFailedAdminLoginAsync(phoneNumber, "Account is deactivated", cancellationToken);
            throw new UnauthorizedAccessException("Account is deactivated");
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        if (!isNewUser)
        {
            _context.Users.Update(user);
        }

        var accessToken = _tokenService.GenerateAccessToken(user.Id, user.PhoneNumber, user.Role?.Name, user.FirstName, user.LastName, user.TokenVersion);
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

        if (user.Role?.Name == "Admin")
        {
            var displayName = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            await _eventService.RecordEventAsync(
                "Auth",
                user.Id.ToString(),
                "AdminLoggedIn",
                null,
                JsonSerializer.Serialize(new { user.Id, user.PhoneNumber }),
                user.Id,
                string.IsNullOrWhiteSpace(displayName) ? user.PhoneNumber : displayName,
                $"Admin logged in ({user.PhoneNumber})",
                user.Id.ToString(),
                cancellationToken);
        }

        return new VerifyCodeResponse(
            accessToken,
            refreshTokenValue,
            _jwtOptions.AccessTokenExpirationMinutes * 60
        );
    }

    private async Task LogFailedAdminLoginAsync(string phoneNumber, string reason, CancellationToken cancellationToken)
    {
        var adminUser = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && u.Role != null && u.Role.Name == "Admin" && !u.IsDeleted, cancellationToken);

        if (adminUser == null) return;

        await _eventService.RecordEventAsync(
            "Auth",
            adminUser.Id.ToString(),
            "AdminLoginFailed",
            null,
            JsonSerializer.Serialize(new { adminUser.Id, phoneNumber, reason }),
            adminUser.Id,
            adminUser.PhoneNumber,
            $"Failed admin login for {phoneNumber} ({reason})",
            adminUser.Id.ToString(),
            cancellationToken);
    }
}
