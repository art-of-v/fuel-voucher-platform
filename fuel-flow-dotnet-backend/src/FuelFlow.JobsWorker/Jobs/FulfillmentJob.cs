using FuelFlow.JobsWorker.Services;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FuelFlow.JobsWorker.Jobs;

public sealed class FulfillmentJob
{
    private readonly IFulfillmentService _fulfillmentService;
    private readonly ILogger<FulfillmentJob> _logger;

    public FulfillmentJob(
        IFulfillmentService fulfillmentService,
        ILogger<FulfillmentJob> logger)
    {
        _fulfillmentService = fulfillmentService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 60, 120 })]
    public async Task ProcessPendingOrdersAsync()
    {
        _logger.LogInformation("Starting fulfillment job");

        try
        {
            await _fulfillmentService.ProcessPendingOrdersAsync();
            _logger.LogInformation("Fulfillment job completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fulfillment job failed");
            throw;
        }
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 60, 120 })]
    public async Task ProcessVoucherImportsAsync()
    {
        _logger.LogInformation("Starting voucher import processing job");

        try
        {
            await _fulfillmentService.ProcessVoucherImportsAsync();
            _logger.LogInformation("Voucher import processing job completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voucher import processing job failed");
            throw;
        }
    }
}
