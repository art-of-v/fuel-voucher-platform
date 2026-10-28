namespace FuelFlow.API.BackgroundJobs.Models;

public sealed class OrderCreatedPayload
{
    /// <summary>
    /// Nullable so a payload that never named its order is rejected as invalid instead of being read
    /// as <see cref="System.Guid.Empty"/> - an id no order has, which the handler then searches for.
    /// </summary>
    public Guid? OrderId { get; set; }

    public string UserId { get; set; } = null!;
}
