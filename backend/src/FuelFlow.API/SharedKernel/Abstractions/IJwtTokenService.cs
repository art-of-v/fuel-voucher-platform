namespace FuelFlow.SharedKernel.Abstractions;

public interface IJwtTokenService
{
    string GenerateAccessToken(Guid userId, string phoneNumber, string? roleName, string? firstName = null, string? lastName = null, int tokenVersion = 1);
    string GenerateRefreshToken();
}
