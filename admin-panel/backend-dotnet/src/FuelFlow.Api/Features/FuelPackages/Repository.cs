using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.FuelPackages;

public partial interface IFuelPackageRepository
{
    Task<IEnumerable<FuelPackage>> GetAllAsync();
    Task<FuelPackage?> GetByIdAsync(string id);
    Task<FuelPackage> CreateAsync(CreateFuelPackageRequest request);
    Task<bool> DeleteAsync(string id);
}

public partial class FuelPackageRepository : IFuelPackageRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public FuelPackageRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<FuelPackage>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<FuelPackage>(
            @"SELECT id, station_id as StationId, fuel_type_id as FuelTypeId, 
              fuel_name as FuelName, liters, price, original_price as OriginalPrice, 
              created_at as CreatedAt 
              FROM fuel_packages ORDER BY created_at DESC");
    }

    public async Task<FuelPackage?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<FuelPackage>(
            @"SELECT id, station_id as StationId, fuel_type_id as FuelTypeId, 
              fuel_name as FuelName, liters, price, original_price as OriginalPrice, 
              created_at as CreatedAt 
              FROM fuel_packages WHERE id = @Id",
            new { Id = id });
    }

    public async Task<FuelPackage> CreateAsync(CreateFuelPackageRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<FuelPackage>(
            @"INSERT INTO fuel_packages (id, station_id, fuel_type_id, fuel_name, liters, price, original_price) 
              VALUES (@Id, @StationId, @FuelTypeId, @FuelName, @Liters, @Price, @OriginalPrice) 
              RETURNING id, station_id as StationId, fuel_type_id as FuelTypeId, 
              fuel_name as FuelName, liters, price, original_price as OriginalPrice, 
              created_at as CreatedAt",
            request);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync("DELETE FROM fuel_packages WHERE id = @Id", new { Id = id });
        return affected > 0;
    }
}
