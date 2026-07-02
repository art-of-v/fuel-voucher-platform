namespace FuelFlow.SharedKernel.Abstractions;

public interface IJwtTokenService
{
    string GenerateAccessToken(Guid userId, string phoneNumber, string? roleName);
    string GenerateRefreshToken();
}
