namespace FuelFlow.Features.Orders.Receipt.GetOrderReceipt;

/// <summary>
/// The order a receipt is built from.
/// </summary>
/// <param name="Id">The order's id. A receipt is named after it.</param>
/// <param name="UserId">
/// The customer the receipt is scoped to, or null for an operator's receipt that may name any
/// order. The scope is applied INSIDE the query, not after the fetch: another customer's order has
/// to be indistinguishable from one that does not exist, otherwise the endpoint enumerates other
/// people's order ids by watching which ids return 404 rather than 403.
/// </param>
public sealed record GetOrderReceiptQuery(Guid Id, Guid? UserId)
{
    /// <summary>The caller's own receipt: scoped to their customer id.</summary>
    public static GetOrderReceiptQuery ForCaller(Guid id, Guid userId) => new(id, userId);

    /// <summary>An operator's receipt: any order the operator may see.</summary>
    public static GetOrderReceiptQuery ForAnyOrder(Guid id) => new(id, null);
}
