using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.Vouchers;

public partial interface IVoucherRepository
{
    Task<IEnumerable<Voucher>> GetUserVouchersAsync(string userId);
    Task<IEnumerable<Voucher>> GetAvailableVouchersAsync();
    Task<Voucher?> UpdateAsync(string id, UpdateVoucherRequest request);
}

public partial class VoucherRepository
{
    public async Task<IEnumerable<Voucher>> GetUserVouchersAsync(string userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Voucher>(
            @"SELECT id, provider, fuel_type as FuelType, amount, 
              qr_code_data as QrCodeData, image_url as ImageUrl, 
              external_id as ExternalId, expiration_date as ExpirationDate, 
              status, created_at as CreatedAt 
              FROM vouchers 
              WHERE assigned_to_user_id = @UserId 
              ORDER BY created_at DESC",
            new { UserId = userId });
    }

    public async Task<IEnumerable<Voucher>> GetAvailableVouchersAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Voucher>(
            @"SELECT id, provider, fuel_type as FuelType, amount, 
              qr_code_data as QrCodeData, image_url as ImageUrl, 
              external_id as ExternalId, expiration_date as ExpirationDate, 
              status, created_at as CreatedAt 
              FROM vouchers 
              WHERE status IN ('imported', 'available') 
              ORDER BY created_at DESC");
    }

    public async Task<Voucher?> UpdateAsync(string id, UpdateVoucherRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        
        var updates = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("Id", Guid.Parse(id));

        if (!string.IsNullOrEmpty(request.Status))
        {
            updates.Add("status = @Status");
            parameters.Add("Status", request.Status);
        }

        if (!string.IsNullOrEmpty(request.AssignedToUserId))
        {
            updates.Add("assigned_to_user_id = @AssignedToUserId");
            parameters.Add("AssignedToUserId", request.AssignedToUserId);
        }

        if (updates.Count == 0)
            return await GetByIdAsync(Guid.Parse(id));

        var sql = $@"UPDATE vouchers SET {string.Join(", ", updates)}, updated_at = NOW() 
                     WHERE id = @Id 
                     RETURNING id, provider, fuel_type as FuelType, amount, 
                     qr_code_data as QrCodeData, image_url as ImageUrl, 
                     external_id as ExternalId, expiration_date as ExpirationDate, 
                     status, created_at as CreatedAt";

        return await connection.QueryFirstOrDefaultAsync<Voucher>(sql, parameters);
    }
}

public record UpdateVoucherRequest
{
    public string? Status { get; init; }
    public string? AssignedToUserId { get; init; }
}
