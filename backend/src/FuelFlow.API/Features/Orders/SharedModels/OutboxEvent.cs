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

/// <summary>
/// The event types that something actually reads.
/// </summary>
/// <remarks>
/// <para>
/// Five of the eight event types have no consumer: they are written and never read, because
/// they are the record of something that happened rather than a request to do something. They
/// are never marked processed, and that is deliberate - marking them processed would be a lie
/// about work nobody did.
/// </para>
/// <para>
/// <b>Why the list is in one place.</b> Two things have to agree on which types have a
/// consumer: the index that the safety net reads through, and the admin's reconciliation
/// report, which counts the unprocessed events and says the fulfilment jobs may be stalled.
/// If the report counts the five consumer-less types - which are most of the table - then the
/// count is always in the thousands and the alarm is always on, which is the same as no alarm.
/// </para>
/// <para>
/// <b>What to do when a type gains a consumer.</b> Add it here. The dispatchers read their own
/// type either way; a type that is missing from this list keeps the old plan and is not
/// counted by the report.
/// </para>
/// </remarks>
public static class ConsumedOutboxEventTypes
{
    /// <summary>
    /// The types the fulfilment service and the notification passes read.
    /// </summary>
    public static readonly OutboxEventType[] All =
    [
        OutboxEventType.OrderCreated,
        OutboxEventType.OrderFulfilled,
        OutboxEventType.VoucherIssued
    ];

    /// <summary>Whether something reads this type.</summary>
    public static bool HasConsumer(OutboxEventType eventType) => All.Contains(eventType);
}
