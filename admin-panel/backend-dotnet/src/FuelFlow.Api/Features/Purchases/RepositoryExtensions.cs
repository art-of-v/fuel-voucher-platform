using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.Purchases;

public partial interface IPurchaseRepository
{
    Task<PurchaseWithQrCode?> GetByIdWithQrCodeAsync(int id);
    Task<IEnumerable<Purchase>> GetByUserIdAsync(string userId);
}

public partial class PurchaseRepository
{
    public async Task<PurchaseWithQrCode?> GetByIdWithQrCodeAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<PurchaseWithQrCode>(
            @"SELECT p.id, p.session_id as SessionId, p.station_name as StationName, 
              p.fuel_name as FuelName, p.liters, p.price, p.status, 
              q.qr_code_url as QrCodeUrl, p.created_at as CreatedAt 
              FROM purchases p 
              LEFT JOIN qr_codes q ON p.qr_code_id = q.id 
              WHERE p.id = @Id",
            new { Id = id });
    }

    public async Task<IEnumerable<Purchase>> GetByUserIdAsync(string userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Purchase>(
            @"SELECT id, session_id as SessionId, package_id as PackageId, 
              station_id as StationId, station_name as StationName, 
              fuel_type as FuelType, fuel_name as FuelName, liters, price, 
              qr_code_id as QrCodeId, voucher_id as VoucherId, status, 
              stripe_session_id as StripeSessionId, created_at as CreatedAt 
              FROM purchases WHERE session_id = @UserId ORDER BY created_at DESC",
            new { UserId = userId });
    }
}
