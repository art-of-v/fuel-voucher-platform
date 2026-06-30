namespace FuelFlow.Features.Auth.SharedAbstractions;

public interface IJwtTokenService
{
    string GenerateAccessToken(Guid userId, string phoneNumber, string? roleName);
    string GenerateRefreshToken();
}
