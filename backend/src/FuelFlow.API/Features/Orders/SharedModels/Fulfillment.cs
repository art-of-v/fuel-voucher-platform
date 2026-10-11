using FuelFlow.Features.Vouchers;

namespace FuelFlow.Features.Orders.SharedModels;

public class Fulfillment
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid VoucherId { get; set; }
    public DateTime FulfilledAtUtc { get; set; }

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
