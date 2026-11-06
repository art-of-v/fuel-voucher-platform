namespace FuelFlow.Payments.SharedModels;

using FuelFlow.Features.Orders.SharedModels;

/// <summary>
/// How much of one held payment pays for one order.
/// </summary>
/// <remarks>
/// <para>
/// The row exists because one transfer is not one sale. Finance declares 1 000,00 ₴ for a customer and
/// that customer then buys twice, for 300 and for 500; the ruling this table encodes is that the
/// remaining 200 is still theirs and still held, and that neither order is paid for by a payment that
/// has been spent. Before it, the only way money could be put on an order was
/// <c>payments.order_id</c> - one column, one order, and so one declared transfer could never cover
/// more than one sale, and a sale costing less than the transfer could not be covered at all without
/// stranding the difference.
/// </para>
/// <para>
/// <b>It is not a second ledger.</b> A capture that names an order in <c>payments.order_id</c> is the
/// whole charge on that order, and it has no rows here: this table draws only on payments which name
/// nothing, which is what makes "the money on this order" a sum of two disjoint sets rather than a
/// number that can double-count one banknote. The database holds that disjointness - an allocation of
/// an attached payment is refused, as is attaching a payment that has been drawn on.
/// </para>
/// <para>
/// <b>Nothing here books revenue.</b> The journal still posts one receipt per settled order at the
/// order's price (<c>PostReceiptToJournalAsync</c>); this table says which money answered for it. That
/// split is deliberate: the payment is a liability until fuel is delivered, and a row that moved money
/// between accounts would report a sale that has not happened yet.
/// </para>
/// <para>
/// A row is written and never edited. The one exception is the draw that the settle itself refuses -
/// the order would not move, so the money goes back to the balance - and that delete happens inside the
/// same transaction that wrote the rows, so no committed allocation is ever revised or removed.
/// </para>
/// </remarks>
public sealed class PaymentAllocation
{
    public Guid Id { get; set; }

    /// <summary>
    /// The money drawn on. A succeeded capture which, when the row was written, named no order at all.
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>The sale the money is now answering for.</summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// How much of the payment this is. The database holds that the rows sharing a
    /// <see cref="PaymentId"/> never sum above that payment's amount.
    /// </summary>
    public decimal AmountUah { get; set; }

    /// <summary>
    /// When the draw was written. Not the money's own date - <c>payments.paid_at_utc</c> stays that -
    /// because two facts differ: when the company received the transfer, and when it decided which
    /// sale the transfer answers for.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    public Payment Payment { get; set; } = null!;
    public Order Order { get; set; } = null!;
}
