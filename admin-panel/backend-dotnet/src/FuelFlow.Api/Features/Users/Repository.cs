using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.Users;

public partial interface IUserRepository
{
    Task<User?> GetByIdAsync(string id);
    Task<User?> GetByPhoneAsync(string phone);
    Task<User?> GetByEmailAsync(string email);
    Task<User> CreateWithPhoneAsync(string phone);
    Task<User> CreateWithEmailAsync(string email);
    Task<User?> UpdateAsync(string id, UpdateUserRequest request);
}

public partial class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(
            @"SELECT id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt 
              FROM users WHERE id = @Id",
            new { Id = id });
    }

    public async Task<User?> GetByPhoneAsync(string phone)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(
            @"SELECT id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt 
              FROM users WHERE phone = @Phone",
            new { Phone = phone });
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(
            @"SELECT id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt 
              FROM users WHERE email = @Email",
            new { Email = email });
    }

    public async Task<User> CreateWithPhoneAsync(string phone)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<User>(
            @"INSERT INTO users (phone) VALUES (@Phone) 
              RETURNING id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt",
            new { Phone = phone });
    }

    public async Task<User> CreateWithEmailAsync(string email)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<User>(
            @"INSERT INTO users (email) VALUES (@Email) 
              RETURNING id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt",
            new { Email = email });
    }

    public async Task<User?> UpdateAsync(string id, UpdateUserRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(
            @"UPDATE users 
              SET first_name = @FirstName, last_name = @LastName, 
                  vehicle_make = @VehicleMake, vehicle_model = @VehicleModel, 
                  vehicle_plate = @VehiclePlate, vehicle_fuel_type = @VehicleFuelType, 
                  updated_at = NOW() 
              WHERE id = @Id 
              RETURNING id, email, phone, first_name as FirstName, last_name as LastName, 
              profile_image_url as ProfileImageUrl, vehicle_make as VehicleMake, 
              vehicle_model as VehicleModel, vehicle_plate as VehiclePlate, 
              vehicle_fuel_type as VehicleFuelType, referral_code as ReferralCode, 
              referred_by as ReferredBy, bonus_balance as BonusBalance, 
              created_at as CreatedAt, updated_at as UpdatedAt",
            new { Id = id, request.FirstName, request.LastName, request.VehicleMake, 
                  request.VehicleModel, request.VehiclePlate, request.VehicleFuelType });
    }
}
