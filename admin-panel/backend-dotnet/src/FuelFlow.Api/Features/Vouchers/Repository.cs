using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.Vouchers;

public partial interface IVoucherRepository
{
    Task<IEnumerable<Voucher>> GetAllAsync();
    Task<Voucher?> GetByIdAsync(Guid id);
    Task<Voucher?> FindAvailableAsync(string provider, string fuelType, int amount);
    Task<IEnumerable<InventoryAggregation>> GetInventoryAggregationAsync();
    Task<bool> DeleteAsync(Guid id);
    Task<int> DeleteAllAsync();
    Task MarkAsUsedAsync(Guid id);
    Task CreateFromImportAsync(Voucher voucher);
}

public partial class VoucherRepository : IVoucherRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public VoucherRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Voucher>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Voucher>(
            @"SELECT id, provider, fuel_type as FuelType, amount, 
              qr_code_data as QrCodeData, image_url as ImageUrl, 
              external_id as ExternalId, expiration_date as ExpirationDate, 
              status, created_at as CreatedAt 
              FROM vouchers ORDER BY created_at DESC");
    }

    public async Task<Voucher?> GetByIdAsync(Guid id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Voucher>(
            @"SELECT id, provider, fuel_type as FuelType, amount, 
              qr_code_data as QrCodeData, image_url as ImageUrl, 
              external_id as ExternalId, expiration_date as ExpirationDate, 
              status, created_at as CreatedAt 
              FROM vouchers WHERE id = @Id",
            new { Id = id });
    }

    public async Task<Voucher?> FindAvailableAsync(string provider, string fuelType, int amount)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Voucher>(
            @"SELECT id, provider, fuel_type as FuelType, amount, 
              qr_code_data as QrCodeData, image_url as ImageUrl, 
              external_id as ExternalId, expiration_date as ExpirationDate, 
              status, created_at as CreatedAt 
              FROM vouchers 
              WHERE provider = @Provider AND fuel_type = @FuelType 
              AND amount = @Amount AND status IN ('imported', 'available') 
              LIMIT 1",
            new { Provider = provider, FuelType = fuelType, Amount = amount });
    }

    public async Task<IEnumerable<InventoryAggregation>> GetInventoryAggregationAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<InventoryAggregation>(
            @"SELECT provider, fuel_type as FuelType, amount as Liters, 
              COUNT(*) as AvailableCount 
              FROM vouchers 
              WHERE status IN ('imported', 'available') 
              GROUP BY provider, fuel_type, amount");
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        using var connection = _connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync("DELETE FROM vouchers WHERE id = @Id", new { Id = id });
        return affected > 0;
    }

    public async Task<int> DeleteAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync("DELETE FROM vouchers");
    }

    public async Task MarkAsUsedAsync(Guid id)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE vouchers SET status = 'used' WHERE id = @Id",
            new { Id = id });
    }

    public async Task CreateFromImportAsync(Voucher voucher)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = """
            INSERT INTO vouchers (
                id, provider, external_id, fuel_type, fuel_subtype, amount, unit,
                expiration_date, status, image_url, qr_code_data, original_file_name,
                source, import_job_id, created_at, updated_at
            ) VALUES (
                @Id, @Provider, @ExternalId, @FuelType, @FuelSubtype, @Amount, @Unit,
                @ExpirationDate, @Status, @ImageUrl, @QrCodeData, @OriginalFileName,
                @Source, @ImportJobId, @CreatedAt, @UpdatedAt
            )
        """;
        await connection.ExecuteAsync(sql, voucher);
    }
}
