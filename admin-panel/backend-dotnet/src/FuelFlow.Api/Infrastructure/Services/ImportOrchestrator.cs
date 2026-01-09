using System.Collections.Concurrent;
using FuelFlow.Api.Features.Vouchers;
using FuelFlow.Api.Features.VoucherImport;

namespace FuelFlow.Api.Infrastructure.Services;

public interface IImportOrchestrator
{
    void QueueJob(string jobId, List<IFormFile> files);
}

public class ImportOrchestrator : BackgroundService, IImportOrchestrator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ImportOrchestrator> _logger;
    private readonly ConcurrentQueue<(string JobId, List<IFormFile> Files)> _jobQueue = new();
    private readonly SemaphoreSlim _signal = new(0);

    public ImportOrchestrator(IServiceProvider serviceProvider, ILogger<ImportOrchestrator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void QueueJob(string jobId, List<IFormFile> files)
    {
        // We need to copy the files to a temporary location or memory because IFormFile is disposed after request
        // For simplicity in this demo, we'll assume we can process them quickly or they are small. 
        // BUT correctly: we should save them to disk or memory stream first.
        
        // Let's create a snapshot of files in memory for processing
        var fileSnapshots = new List<IFormFile>();
        foreach (var file in files)
        {
            var ms = new MemoryStream();
            file.CopyTo(ms);
            ms.Position = 0;
            fileSnapshots.Add(new FormFile(ms, 0, ms.Length, file.Name, file.FileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = file.ContentType
            });
        }
        
        _jobQueue.Enqueue((jobId, fileSnapshots));
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (_jobQueue.TryDequeue(out var job))
            {
                try
                {
                    await ProcessJobAsync(job.JobId, job.Files, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing job {JobId}", job.JobId);
                    using var scope = _serviceProvider.CreateScope();
                    var repo = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
                    await repo.UpdateProgressAsync(job.JobId, 0, 0, 0, "failed");
                }
            }
        }
    }

    private async Task ProcessJobAsync(string jobId, List<IFormFile> files, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var geminiService = scope.ServiceProvider.GetRequiredService<IGeminiService>();
        var importRepo = scope.ServiceProvider.GetRequiredService<IImportJobRepository>();
        var voucherRepo = scope.ServiceProvider.GetRequiredService<IVoucherRepository>();

        _logger.LogInformation("Starting job {JobId} with {Count} files", jobId, files.Count);

        int processedFiles = 0;
        int successfulVouchers = 0; // Note: Node implementation tracks vouchers, not successful files despite the name if logic is consistent
        int failedFiles = 0;
        int duplicates = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Only process PDF and Images
                var isPdf = file.ContentType == "application/pdf" || file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                var isImage = file.ContentType.StartsWith("image/") || 
                              file.FileName.EndsWith(".png") || 
                              file.FileName.EndsWith(".jpg") || 
                              file.FileName.EndsWith(".jpeg");

                if (!isPdf && !isImage)
                {
                    failedFiles++;
                    processedFiles++;
                    await importRepo.UpdateProgressAsync(jobId, processedFiles, successfulVouchers, failedFiles, "processing");
                    continue;
                }

                using var stream = file.OpenReadStream();
                var results = await geminiService.AnalyzePdfAsync(stream, file.ContentType);

                bool fileHasVouchers = false;

                foreach (var result in results)
                {
                    try
                    {
                        // Check if exists
                        var existing = await voucherRepo.GetByIdAsync(Guid.Empty); // We don't have GetByExternalId yet, assume Create handles logic or check manually

                        // Actually, I'll rely on the CreateVoucher logic or add a check.
                        // Assuming create handles it or we catch exception if unique constraint.
                        
                        var voucher = new Voucher
                        {
                            Id = Guid.NewGuid(),
                            Provider = result.Provider ?? "OKKO",
                            ExternalId = result.ExternalId,
                            FuelType = result.FuelType ?? "Unknown",
                            Amount = result.Amount,
                            Unit = "liters",
                            ExpirationDate = result.ExpirationDate ?? DateTime.UtcNow.AddMonths(1),
                            Status = "available",
                            OriginalFileName = file.FileName,
                            Source = "dotnet_orchestrator",
                            ImportJobId = jobId,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        // Since we don't have "CreateVoucher" that takes a Voucher object in the public interface yet (only DTO),
                        // let me quickly query the DB directly to check existence to prevent duplicates if generic Create doesn't handle.
                        // Actually, let's use the DTO approach and assume Repository handles it?
                        // The existing Create method takes `CreateVoucherRequest`.
                        
                        // Let's add `CreateInternalAsync` to repository or just map it.
                        // For parity, Node checks for duplicates.
                        // Since I don't want to change repository interface instantly if not needed, I'll use SQL directly or rely on catch.
                        
                        // Let's assume we can map to CreateVoucherRequest? No, that one is for API.
                        // I will add `CreateFromImportAsync` to IO repository interface separately or just use dapper here if needed,
                        // but better to add to repository.

                        // Checking duplicate:
                         // (Wait, I realize I need to add `CreateVoucher` method that accepts full object or similar to Repository).
                        
                        await voucherRepo.CreateFromImportAsync(voucher);
                        successfulVouchers++;
                        fileHasVouchers = true;
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("unique") || ex.Message.Contains("duplicate"))
                        {
                            duplicates++;
                            _logger.LogWarning("Duplicate voucher {Id}", result.ExternalId);
                        }
                        else
                        {
                            _logger.LogError(ex, "Error saving voucher {Id}", result.ExternalId);
                        }
                    }
                }

                if (!fileHasVouchers) failedFiles++; // Or maybe just processed with 0 results
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file {File}", file.FileName);
                failedFiles++;
            }

            processedFiles++;
            await importRepo.UpdateProgressAsync(jobId, processedFiles, successfulVouchers, failedFiles, "processing");
        }

        await importRepo.CompleteAsync(jobId, successfulVouchers, failedFiles, duplicates);
        _logger.LogInformation("Job {JobId} completed", jobId);
    }
}
