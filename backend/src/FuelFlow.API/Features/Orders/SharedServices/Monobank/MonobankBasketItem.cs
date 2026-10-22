namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

/// <summary>
/// One line of the fiscal receipt, as Monobank's merchant API wants it.
///
/// Monobank registers the receipt with the tax authority on our behalf, but it only knows what we
/// told it. <c>merchantPaymInfo.basketOrder</c> is that: without it the receipt carries a total and
/// no goods lines, and a receipt with no goods lines does not describe a sale of fuel.
///
/// Sent whether or not fiscalisation (ПРРО) is currently active. The link is switched on in the
/// Monobank web cabinet without notice to us, and at that moment every sale and every refund
/// becomes a receipt that fails. Building it always means switching it on is not a launch-day event.
/// </summary>
public sealed class MonobankBasketItem
{
    /// <summary>What was sold, in words a customer and an inspector can read: "А-95, 10 л".</summary>
    public string Name { get; set; } = null!;

    /// <summary>How many. Litres for fuel, not packages - that is the unit the customer buys in.</summary>
    public decimal Qty { get; set; }

    /// <summary>Price for ONE <see cref="Qty"/>, in kopecks. Monobank's own example splits it this way.</summary>
    public long Sum { get; set; }

    /// <summary><see cref="Sum"/> × <see cref="Qty"/>, in kopecks.</summary>
    public long Total { get; set; }

    /// <summary>"л" for fuel. Without a unit the receipt reads as a bare number.</summary>
    public string Unit { get; set; } = null!;

    /// <summary>Our stable identifier for the line, so a refund can name the same thing.</summary>
    public string? Code { get; set; }
}
