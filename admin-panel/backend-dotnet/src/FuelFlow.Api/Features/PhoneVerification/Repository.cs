using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.PhoneVerification;

public interface IPhoneVerificationRepository
{
    Task CreateAsync(string phone, string code);
    Task<PhoneVerification?> GetLatestAsync(string phone);
    Task MarkVerifiedAsync(int id);
}

public class PhoneVerificationRepository : IPhoneVerificationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PhoneVerificationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(string phone, string code)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO phone_verifications (phone, code, expires_at) 
              VALUES (@Phone, @Code, @ExpiresAt)",
            new { Phone = phone, Code = code, ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
    }

    public async Task<PhoneVerification?> GetLatestAsync(string phone)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<PhoneVerification>(
            @"SELECT id, phone, code, expires_at as ExpiresAt, verified, created_at as CreatedAt 
              FROM phone_verifications 
              WHERE phone = @Phone AND expires_at > NOW() 
              ORDER BY created_at DESC LIMIT 1",
            new { Phone = phone });
    }

    public async Task MarkVerifiedAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE phone_verifications SET verified = 1 WHERE id = @Id",
            new { Id = id });
    }
}
