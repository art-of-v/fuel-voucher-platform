using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.QrCodes;

public interface IQrCodeRepository
{
    Task<IEnumerable<QrCode>> GetAllAsync();
    Task<QrCode?> GetByIdAsync(int id);
    Task<QrCode?> FindAvailableAsync(string stationId, string fuelType, int liters);
    Task<QrCode> CreateAsync(CreateQrCodeRequest request);
    Task<bool> DeleteAsync(int id);
    Task MarkAsSoldAsync(int id, int purchaseId);
}

public class QrCodeRepository : IQrCodeRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public QrCodeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<QrCode>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<QrCode>(
            @"SELECT id, station_id as StationId, fuel_type as FuelType, liters, 
              qr_code_url as QrCodeUrl, status, purchase_id as PurchaseId, 
              created_at as CreatedAt 
              FROM qr_codes ORDER BY created_at DESC");
    }

    public async Task<QrCode?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<QrCode>(
            @"SELECT id, station_id as StationId, fuel_type as FuelType, liters, 
              qr_code_url as QrCodeUrl, status, purchase_id as PurchaseId, 
              created_at as CreatedAt 
              FROM qr_codes WHERE id = @Id",
            new { Id = id });
    }

    public async Task<QrCode?> FindAvailableAsync(string stationId, string fuelType, int liters)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<QrCode>(
            @"SELECT id, station_id as StationId, fuel_type as FuelType, liters, 
              qr_code_url as QrCodeUrl, status, purchase_id as PurchaseId, 
              created_at as CreatedAt 
              FROM qr_codes 
              WHERE station_id = @StationId AND fuel_type = @FuelType 
              AND liters = @Liters AND status = 'available' 
              LIMIT 1",
            new { StationId = stationId, FuelType = fuelType, Liters = liters });
    }

    public async Task<QrCode> CreateAsync(CreateQrCodeRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<QrCode>(
            @"INSERT INTO qr_codes (station_id, fuel_type, liters, qr_code_url, status) 
              VALUES (@StationId, @FuelType, @Liters, @QrCodeUrl, 'available') 
              RETURNING id, station_id as StationId, fuel_type as FuelType, liters, 
              qr_code_url as QrCodeUrl, status, purchase_id as PurchaseId, 
              created_at as CreatedAt",
            request);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync("DELETE FROM qr_codes WHERE id = @Id", new { Id = id });
        return affected > 0;
    }

    public async Task MarkAsSoldAsync(int id, int purchaseId)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE qr_codes SET status = 'sold', purchase_id = @PurchaseId WHERE id = @Id",
            new { Id = id, PurchaseId = purchaseId });
    }
}
