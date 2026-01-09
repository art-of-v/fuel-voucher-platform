using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.Purchases;

public partial interface IPurchaseRepository
{
    Task<IEnumerable<Purchase>> GetAllAsync();
    Task<IEnumerable<PurchaseWithQrCode>> GetBySessionIdAsync(string sessionId);
    Task<Purchase?> GetByIdAsync(int id);
    Task<Purchase?> GetByStripeSessionIdAsync(string stripeSessionId);
    Task<Purchase> CreateAsync(CreatePurchaseRequest request);
    Task UpdateStatusAsync(int id, string status);
    Task AssignQrCodeAsync(int purchaseId, int qrCodeId);
    Task AssignVoucherAsync(int purchaseId, Guid voucherId);
    Task SetStripeSessionIdAsync(int id, string stripeSessionId);
}

public partial class PurchaseRepository : IPurchaseRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PurchaseRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Purchase>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Purchase>(
            @"SELECT id, session_id as SessionId, package_id as PackageId, 
              station_id as StationId, station_name as StationName, 
              fuel_type as FuelType, fuel_name as FuelName, liters, price, 
              qr_code_id as QrCodeId, voucher_id as VoucherId, status, 
              stripe_session_id as StripeSessionId, created_at as CreatedAt 
              FROM purchases ORDER BY created_at DESC");
    }

    public async Task<IEnumerable<PurchaseWithQrCode>> GetBySessionIdAsync(string sessionId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<PurchaseWithQrCode>(
            @"SELECT p.id, p.session_id as SessionId, p.station_name as StationName, 
              p.fuel_name as FuelName, p.liters, p.price, p.status, 
              q.qr_code_url as QrCodeUrl, p.created_at as CreatedAt 
              FROM purchases p 
              LEFT JOIN qr_codes q ON p.qr_code_id = q.id 
              WHERE p.session_id = @SessionId 
              ORDER BY p.created_at DESC",
            new { SessionId = sessionId });
    }

    public async Task<Purchase?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Purchase>(
            @"SELECT id, session_id as SessionId, package_id as PackageId, 
              station_id as StationId, station_name as StationName, 
              fuel_type as FuelType, fuel_name as FuelName, liters, price, 
              qr_code_id as QrCodeId, voucher_id as VoucherId, status, 
              stripe_session_id as StripeSessionId, created_at as CreatedAt 
              FROM purchases WHERE id = @Id",
            new { Id = id });
    }

    public async Task<Purchase?> GetByStripeSessionIdAsync(string stripeSessionId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Purchase>(
            @"SELECT id, session_id as SessionId, package_id as PackageId, 
              station_id as StationId, station_name as StationName, 
              fuel_type as FuelType, fuel_name as FuelName, liters, price, 
              qr_code_id as QrCodeId, voucher_id as VoucherId, status, 
              stripe_session_id as StripeSessionId, created_at as CreatedAt 
              FROM purchases WHERE stripe_session_id = @StripeSessionId",
            new { StripeSessionId = stripeSessionId });
    }

    public async Task<Purchase> CreateAsync(CreatePurchaseRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<Purchase>(
            @"INSERT INTO purchases (session_id, package_id, station_id, station_name, 
              fuel_type, fuel_name, liters, price, status) 
              VALUES (@SessionId, @PackageId, @StationId, @StationName, 
              @FuelType, @FuelName, @Liters, @Price, 'pending') 
              RETURNING id, session_id as SessionId, package_id as PackageId, 
              station_id as StationId, station_name as StationName, 
              fuel_type as FuelType, fuel_name as FuelName, liters, price, 
              qr_code_id as QrCodeId, voucher_id as VoucherId, status, 
              stripe_session_id as StripeSessionId, created_at as CreatedAt",
            request);
    }

    public async Task UpdateStatusAsync(int id, string status)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE purchases SET status = @Status WHERE id = @Id",
            new { Id = id, Status = status });
    }

    public async Task AssignQrCodeAsync(int purchaseId, int qrCodeId)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE purchases SET qr_code_id = @QrCodeId, status = 'delivered' WHERE id = @PurchaseId",
            new { PurchaseId = purchaseId, QrCodeId = qrCodeId });
    }

    public async Task AssignVoucherAsync(int purchaseId, Guid voucherId)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE purchases SET voucher_id = @VoucherId, status = 'delivered' WHERE id = @PurchaseId",
            new { PurchaseId = purchaseId, VoucherId = voucherId });
    }

    public async Task SetStripeSessionIdAsync(int id, string stripeSessionId)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE purchases SET stripe_session_id = @StripeSessionId WHERE id = @Id",
            new { Id = id, StripeSessionId = stripeSessionId });
    }
}
