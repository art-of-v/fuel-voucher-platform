using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.FuelTypes;

public interface IFuelTypeRepository
{
    Task<IEnumerable<FuelType>> GetAllAsync();
    Task<FuelType?> GetByIdAsync(string id);
    Task<FuelType> CreateAsync(CreateFuelTypeRequest request);
    Task<FuelType?> UpdateAsync(string id, UpdateFuelTypeRequest request);
    Task<bool> DeleteAsync(string id);
}

public class FuelTypeRepository : IFuelTypeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public FuelTypeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<FuelType>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<FuelType>(
            @"SELECT id, name, station_id as StationId, base_price as BasePrice, 
              discount_price as DiscountPrice, created_at as CreatedAt 
              FROM fuel_types ORDER BY created_at DESC");
    }

    public async Task<FuelType?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<FuelType>(
            @"SELECT id, name, station_id as StationId, base_price as BasePrice, 
              discount_price as DiscountPrice, created_at as CreatedAt 
              FROM fuel_types WHERE id = @Id",
            new { Id = id });
    }

    public async Task<FuelType> CreateAsync(CreateFuelTypeRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<FuelType>(
            @"INSERT INTO fuel_types (id, name, station_id, base_price, discount_price) 
              VALUES (@Id, @Name, @StationId, @BasePrice, @DiscountPrice) 
              RETURNING id, name, station_id as StationId, base_price as BasePrice, 
              discount_price as DiscountPrice, created_at as CreatedAt",
            request);
    }

    public async Task<FuelType?> UpdateAsync(string id, UpdateFuelTypeRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<FuelType>(
            @"UPDATE fuel_types 
              SET name = @Name, base_price = @BasePrice, discount_price = @DiscountPrice 
              WHERE id = @Id 
              RETURNING id, name, station_id as StationId, base_price as BasePrice, 
              discount_price as DiscountPrice, created_at as CreatedAt",
            new { Id = id, request.Name, request.BasePrice, request.DiscountPrice });
    }

    public async Task<bool> DeleteAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync("DELETE FROM fuel_types WHERE id = @Id", new { Id = id });
        return affected > 0;
    }
}
