namespace FuelFlow.API.BackgroundJobs.Models;

public sealed class OrderCreatedPayload
{
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = null!;
}
