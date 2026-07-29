namespace FuelFlow.SharedKernel.Abstractions;

public interface IJwtTokenService
{
    string GenerateAccessToken(Guid userId, string phoneNumber, string? roleName, string? firstName = null, string? lastName = null);
    string GenerateRefreshToken();
}
