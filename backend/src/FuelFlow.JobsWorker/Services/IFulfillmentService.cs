namespace FuelFlow.JobsWorker.Services;

public interface IFulfillmentService
{
    Task ProcessPendingOrdersAsync(CancellationToken cancellationToken = default);
    Task ProcessVoucherImportsAsync(CancellationToken cancellationToken = default);
}
