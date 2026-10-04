namespace FuelFlow.Features.Orders.SharedModels;

/// <summary>
/// What an order represents. Stored as the enum name, so the database reads
/// <c>Purchase</c> / <c>Renewal</c> / <c>ReceivedFromCompany</c> verbatim.
/// </summary>
public enum OrderKind
{
    /// <summary>Fuel bought for money. The only kind that carries revenue.</summary>
    Purchase = 0,

    /// <summary>A customer's own voucher extended or replaced, paid for by that customer.</summary>
    Renewal = 1,

    /// <summary>
    /// Fuel a company handed to one of its workers. Not a sale: it moves fuel that was already
    /// bought by the company from the pool into someone's hands, so it must stay out of every
    /// revenue, margin and reconciliation total. <c>SourceOrderId</c> points at the purchase it
    /// came from.
    /// </summary>
    ReceivedFromCompany = 2
}