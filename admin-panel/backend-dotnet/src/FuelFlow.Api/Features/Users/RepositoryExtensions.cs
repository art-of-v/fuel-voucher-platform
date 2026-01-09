using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.Users;

public partial interface IUserRepository
{
    Task<User?> GetByReferralCodeAsync(string code);
    Task<User?> UpdateReferralCodeAsync(string userId, string code);
    Task UpdateReferredByAsync(string userId, string referrerId);
    Task UpdateBonusBalanceAsync(string userId, int balance);
    Task<IEnumerable<User>> GetAllAsync();
}

public partial class UserRepository
{
    public async Task<User?> GetByReferralCodeAsync(string code)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(
            @"SELECT id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt 
              FROM users WHERE referral_code = @Code",
            new { Code = code });
    }

    public async Task<User?> UpdateReferralCodeAsync(string userId, string code)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(
            @"UPDATE users SET referral_code = @Code, updated_at = NOW() 
              WHERE id = @UserId 
              RETURNING id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt",
            new { UserId = userId, Code = code });
    }

    public async Task UpdateReferredByAsync(string userId, string referrerId)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE users SET referred_by = @ReferrerId, updated_at = NOW() WHERE id = @UserId",
            new { UserId = userId, ReferrerId = referrerId });
    }

    public async Task UpdateBonusBalanceAsync(string userId, int balance)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE users SET bonus_balance = @Balance, updated_at = NOW() WHERE id = @UserId",
            new { UserId = userId, Balance = balance });
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<User>(
            @"SELECT id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt 
              FROM users ORDER BY created_at DESC");
    }
}
