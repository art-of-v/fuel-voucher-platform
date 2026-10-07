using FuelFlow.Features.Vouchers;

namespace FuelFlow.Features.Orders.SharedModels;

public class Fulfillment
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid VoucherId { get; set; }
    public DateTime FulfilledAtUtc { get; set; }

    public FuelVoucher? Voucher { get; set; }

    /// <summary>
    /// The order that delivered this voucher. Required, and mapped explicitly in
    /// <c>FulfillmentConfiguration</c>: left to convention EF scaffolds a second, shadow foreign key
    /// column instead of using the existing <see cref="OrderId"/>.
    /// </summary>
    public Order Order { get; set; } = null!;
}
