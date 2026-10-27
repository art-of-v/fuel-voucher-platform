using FuelFlow.SharedKernel;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

/// <summary>
/// Turns order lines into the receipt lines Monobank registers with the tax authority.
///
/// The basket path and the refund path both need this, and they must not disagree: a receipt
/// whose goods lines do not match the order is worse than one with no goods lines, because it is
/// wrong in a way an inspector will catch.
/// </summary>
public static class MonobankBasketBuilder
{
    /// <summary>
    /// What every line sells: the advertising service the QR code is bought under.
    /// </summary>
    /// <remarks>
    /// Not the fuel. The station sells and dispenses the fuel; the platform sells the placement of
    /// the promotional code, and the customer's own PDF receipt says the same words
    /// (<see cref="FuelFlow.Features.Orders.Receipt.OrderReceiptDocumentBuilder.ServiceLabel"/>).
    /// Two documents describing one payment in two vocabularies is what an inspector asks about.
    /// </remarks>
    internal const string ServiceLabel = "Рекламні послуги";

    /// <summary>The unit <see cref="ServiceLabel"/> is counted in - codes sold, never litres.</summary>
    internal const string ServiceUnit = "послуга";

    /// <summary>
    /// One receipt line per order line.
    /// <para>
    /// <c>Qty</c> is codes, and <c>Sum</c> is the price of ONE code, because
    /// <see cref="MonobankBasketItem.Total"/> is defined as <c>Sum × Qty</c>. <c>UnitPrice</c> on the
    /// order line is a package price - a 10 L code ordered once is one unit for 862.00, not ten
    /// units - so the litres this used to count made the line declare a per-litre price ten times
    /// what it charged.
    /// </para>
    /// <para>
    /// A refund passes lines describing only the unfulfilled part, so a half-returned order yields
    /// half the codes rather than the whole line.
    /// </para>
    /// <para>
    /// The fuel survives only as <c>Code</c>: the id of the fuel type the code is redeemable for,
    /// which is what lets a refund name the same line back. It is not the name of the sale, and a
    /// uuid is not something a customer can read on a receipt.
    /// </para>
    /// </summary>
    public static List<MonobankBasketItem> Build(
        IEnumerable<(Guid FuelTypeId, decimal Liters, int Quantity, decimal UnitPrice, decimal LineTotal)> lines)
    {
        var items = new List<MonobankBasketItem>();

        foreach (var line in lines)
        {
            // The litres guard is not arithmetic here, it is stock: a line whose volume is zero
            // describes no code that could be handed over, whatever its quantity claims.
            if (line.Quantity <= 0 || line.Liters <= 0) continue;

            items.Add(new MonobankBasketItem
            {
                Name = ServiceLabel,
                Qty = line.Quantity,
                Sum = (long)Math.Round(Money.ToKopecks(line.UnitPrice), MidpointRounding.AwayFromZero),
                Total = (long)Math.Round(Money.ToKopecks(line.LineTotal), MidpointRounding.AwayFromZero),
                Unit = ServiceUnit,
                Code = line.FuelTypeId.ToString(),
            });
        }

        return items;
    }
}
