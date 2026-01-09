using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.FuelPackages;

public partial interface IFuelPackageRepository
{
    Task<IEnumerable<FuelPackage>> GetByStationAsync(string stationId);
    Task<FuelPackage?> UpdateAsync(string id, UpdateFuelPackageRequest request);
}

public partial class FuelPackageRepository
{
    public async Task<IEnumerable<FuelPackage>> GetByStationAsync(string stationId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<FuelPackage>(
            @"SELECT id, station_id as StationId, fuel_type_id as FuelTypeId, 
              fuel_name as FuelName, liters, price, original_price as OriginalPrice, 
              created_at as CreatedAt 
              FROM fuel_packages WHERE station_id = @StationId ORDER BY created_at DESC",
            new { StationId = stationId });
    }

    public async Task<FuelPackage?> UpdateAsync(string id, UpdateFuelPackageRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<FuelPackage>(
            @"UPDATE fuel_packages 
              SET fuel_name = @FuelName, liters = @Liters, price = @Price, original_price = @OriginalPrice 
              WHERE id = @Id 
              RETURNING id, station_id as StationId, fuel_type_id as FuelTypeId, 
              fuel_name as FuelName, liters, price, original_price as OriginalPrice, 
              created_at as CreatedAt",
            new { Id = id, request.FuelName, request.Liters, request.Price, request.OriginalPrice });
    }
}

public record UpdateFuelPackageRequest
{
    public string FuelName { get; init; } = string.Empty;
    public int Liters { get; init; }
    public int Price { get; init; }
    public int OriginalPrice { get; init; }
}
