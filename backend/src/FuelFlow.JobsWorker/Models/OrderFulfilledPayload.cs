namespace FuelFlow.JobsWorker.Models;

public sealed class OrderFulfilledPayload
{
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = null!;
    public DateTime FulfilledAt { get; set; }
}
