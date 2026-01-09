using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.VoucherImport;

public partial interface IImportJobRepository
{
    Task<ImportJob> CreateAsync(int totalFiles);
    Task<ImportJob?> GetByIdAsync(string id);
    Task UpdateProgressAsync(string id, int processed, int successful, int failed, int duplicates, string status);
    Task CompleteAsync(string id, int successful, int failed, int duplicates);
}

public partial class ImportJobRepository : IImportJobRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ImportJobRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ImportJob> CreateAsync(int totalFiles)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<ImportJob>(
            @"INSERT INTO import_jobs (admin_id, total_files, status) 
              VALUES (@AdminId, @TotalFiles, 'processing') 
              RETURNING id, admin_id as AdminId, total_files as TotalFiles, 
              processed_files as ProcessedFiles, successful_files as SuccessfulFiles, 
              failed_files as FailedFiles, duplicate_vouchers as DuplicateVouchers, 
              status, model_used as ModelUsed, created_at as CreatedAt, updated_at as UpdatedAt",
            new { AdminId = "admin", TotalFiles = totalFiles });
    }

    public async Task<ImportJob?> GetByIdAsync(string id)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<ImportJob>(
            @"SELECT id, admin_id as AdminId, total_files as TotalFiles, 
              processed_files as ProcessedFiles, successful_files as SuccessfulFiles, 
              failed_files as FailedFiles, duplicate_vouchers as DuplicateVouchers, 
              status, model_used as ModelUsed, created_at as CreatedAt, updated_at as UpdatedAt 
              FROM import_jobs WHERE id = @Id::uuid",
            new { Id = id });
    }

    public async Task UpdateProgressAsync(string id, int processed, int successful, int failed, int duplicates, string status)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"UPDATE import_jobs 
              SET processed_files = @Processed, successful_files = @Successful, 
                  failed_files = @Failed, duplicate_vouchers = @Duplicates, status = @Status,
                  updated_at = NOW() 
              WHERE id = @Id::uuid",
            new { Id = id, Processed = processed, Successful = successful, Failed = failed, Duplicates = duplicates, Status = status });
    }

    public async Task CompleteAsync(string id, int successful, int failed, int duplicates)
    {
        using var connection = _connectionFactory.CreateConnection();
        // Determine status based on failures
        var status = failed > 0 ? "completed_with_errors" : "completed";
        
        await connection.ExecuteAsync(
            @"UPDATE import_jobs 
              SET successful_files = @Successful, failed_files = @Failed, 
                  duplicate_vouchers = @Duplicates, status = @Status, 
                  updated_at = NOW() 
              WHERE id = @Id::uuid",
            new { Id = id, Successful = successful, Failed = failed, Duplicates = duplicates, Status = status });
    }
}
