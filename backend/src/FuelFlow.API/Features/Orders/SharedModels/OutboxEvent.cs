namespace FuelFlow.Features.Orders.SharedModels;

public class OutboxEvent
{
    public int Id { get; set; }
    public OutboxEventType EventType { get; set; }
    public string Payload { get; set; } = null!;
    public bool Processed { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
