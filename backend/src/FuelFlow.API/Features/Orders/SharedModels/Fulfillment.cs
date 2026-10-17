using FuelFlow.Features.Vouchers;

namespace FuelFlow.Features.Orders.SharedModels;

public class Fulfillment
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid VoucherId { get; set; }
    public DateTime FulfilledAtUtc { get; set; }

    /// <summary>
    /// Which line of the order this voucher satisfied.
    /// </summary>
    /// <remarks>
    /// <b>Nullable, and that is the honest state of the data.</b> A fulfilment without it says only
    /// that the order delivered a voucher - which is what every row said before this column existed,
    /// and what the backfill could not improve on where the match was ambiguous. Historical rows that
    /// matched exactly one line were filled in; the rest stay null rather than being guessed at.
    /// </remarks>
    /// <para>
    /// <b>Why it is more than a reporting convenience.</b> The reconciliation counts line items
    /// against fulfilments, and until now it did so grouped by <em>litres alone</em> - which cannot
    /// distinguish two lines of the same volume. An order for "OKKO A95 10 L x2" and "WOG A95 10 L x1"
    /// that received three OKKO vouchers and no WOG one reported <c>10 L: 3</c> both sides and passed.
    /// The customer is short a line, the operator sees a fulfilled order, and the repair logic - which
    /// releases the excess out of the same litres bucket - has no way to tell which delivery was
    /// wrong. With the line id the comparison is per line, and so is the repair.
    /// </para>
    /// <para>
    /// A renewal order carries line items too (one per renewed voucher, naming it in
    /// <c>SourceVoucherId</c>), so they are attributed the same way. This column is also what answers
    /// "which voucher did that renewal deliver" now that <c>voucher_renewal_items</c> is gone, and it
    /// is what makes the correspondence exact rather than positional.
    /// </para>
    /// </remarks>
    public Guid? LineItemId { get; set; }

    /// <summary>
    /// Set when the reconciliation takes a voucher back off this order, null while the delivery
    /// stands. The row is kept either way (#217): the fulfillment records that a delivery happened,
    /// and the correction is the fact that it was undone - not a reason to erase the first part.
    /// </summary>
    public DateTime? ReleasedAtUtc { get; set; }

    /// <summary>
    /// Why the voucher was returned to stock. Null while the delivery stands.
    /// </summary>
    public string? ReleaseReason { get; set; }

    public FuelVoucher? Voucher { get; set; }

    /// <summary>
    /// The order that delivered this voucher. Required, and mapped explicitly in
    /// <c>FulfillmentConfiguration</c>: left to convention EF scaffolds a second, shadow foreign key
    /// column instead of using the existing <see cref="OrderId"/>.
    /// </summary>
    public Order Order { get; set; } = null!;
}
