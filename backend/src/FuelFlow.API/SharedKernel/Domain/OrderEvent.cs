using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// An append-only record of one thing that happened to an order.
/// </summary>
/// <remarks>
/// <para>
/// <c>orders</c> is mutated in place: <c>Status</c>, <c>MonobankStatus</c>,
/// <c>PartiallyFulfilledSinceUtc</c> and the <c>last_webhook_*</c> columns all change as the order
/// moves. That made the current row the only record of the order's life, which is not enough for two
/// jobs this product has.
/// </para>
/// <para>
/// <b>Reconciliation.</b> The payment path is webhook-driven and settlement is monthly against a
/// Monobank statement. A webhook delivered twice, or out of order, produces exactly the same final
/// row as a clean run — so after the fact there is no way to tell them apart, and no per-state
/// timestamp to assert that the invoice existed before the payment was captured.
/// </para>
/// <para>
/// <b>Support.</b> "What happened to my order?" is the question every operator gets asked, and today
/// the answer is whatever the row happens to say now.
/// </para>
/// <para>
/// Deliberately not a second outbox. <c>provider_event_outbox</c> is the admin/audit trail and has a
/// different retention life; this is the state machine of one order.
/// </para>
/// <para>
/// <b>Nothing updates or deletes a row here.</b> No query filter, no soft delete, no cascade from the
/// order either way beyond the one Remove: an order that is deleted takes its history with it,
/// which is the point of keeping it.
/// </para>
/// </remarks>
public sealed class OrderEvent : IAppendOnlyEntity
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>
    /// What happened: <c>Created</c>, <c>InvoiceCreated</c>, <c>PaymentCaptured</c>,
    /// <c>Fulfilled</c>, <c>PartiallyFulfilled</c>, <c>Refunded</c>, <c>Cancelled</c>,
    /// <c>StatusChanged</c>.
    /// </summary>
    /// <remarks>
    /// A <c>varchar</c> rather than an enum column, matching how <c>orders.status</c> and
    /// <c>outbox_events.event_type</c> are already stored. Pinned by a CHECK so a typo cannot land:
    /// the same treatment those two already get.
    /// </remarks>
    public string EventType { get; set; } = null!;

    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }

    /// <summary>
    /// The external reference this transition came from - a Monobank invoice id, usually. Kept
    /// denormalised so the same event can be found without joining the order, which matters because
    /// this is exactly what a duplicate-delivery investigation looks for.
    /// </summary>
    public string? ExternalRef { get; set; }

    /// <summary>
    /// The raw payload that caused the transition, where one exists (a Monobank webhook body).
    /// Kept because reconciling against a statement means being able to see what the acquirer said,
    /// not only what we concluded from it.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Who caused it: a user id, a Telegram staff id, or null for a system transition. Not a
    /// foreign key, on the same reasoning as every other audit actor column in this schema - see
    /// issue #307, where that rule is written down.
    /// </summary>
    public Guid? ActorUserId { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}