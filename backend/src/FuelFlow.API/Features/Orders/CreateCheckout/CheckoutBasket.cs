using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// The identity of a basket: what was bought, by whom, in one checkout. It is the value the
/// checkout guard stores on the order and the database indexes, so two requests for the same
/// basket are recognisably the same request and only one of them becomes an order.
/// </summary>
/// <remarks>
/// <para>
/// The key names the basket and nothing else. No clock, no random suffix, no attempt counter: a
/// key that carries any of those stops being the basket's identity and becomes the attempt's,
/// which is how a retry that lands a second later used to miss the dedupe and charge the customer
/// twice for one tap.
/// </para>
/// <para>
/// The digest is what keeps the key inside the column and free of anything that would change
/// between two requests for the same basket: the litres are written in the invariant culture, so a
/// server running under a comma-decimal locale produces the same key as one running under a dot.
/// The basket itself is still readable on the order - the line items say what was bought - so the
/// digest costs nothing when somebody opens the database to look.
/// </para>
/// </remarks>
public static class CheckoutBasket
{
    /// <summary>The prefix of a single-fuel purchase. It keeps the three kinds of basket apart.</summary>
    public const string Purchase = "buy";

    /// <summary>The prefix of a cart checkout - the path the mobile app actually uses.</summary>
    public const string Cart = "bulk";

    /// <summary>The prefix of a renewal checkout.</summary>
    public const string Renewal = "renew";

    /// <summary>
    /// The key of a basket, for the given customer. The same basket for a different customer is a
    /// different key: one customer's purchase must never hold another customer's basket.
    /// </summary>
    /// <param name="kind">One of the prefixes above - what sort of checkout this is.</param>
    /// <param name="userId">The customer the basket belongs to.</param>
    /// <param name="signature">What is in the basket, written the same way every time.</param>
    public static string Key(string kind, Guid userId, string signature)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..32];
        return $"{kind}:{userId:N}:{digest}";
    }

    /// <summary>
    /// The signature of a single-fuel purchase: the brand, the fuel, the litres and the quantity.
    /// </summary>
    public static string PurchaseSignature(Guid brandId, Guid fuelTypeId, decimal liters, int quantity)
        => string.Join(
            ":",
            brandId.ToString("N"),
            fuelTypeId.ToString("N"),
            liters.ToString(CultureInfo.InvariantCulture),
            quantity.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The signature of a cart: every line, in an order that does not depend on the order the
    /// customer picked the lines in. Two carts with the same lines in a different order are the
    /// same basket, and the customer's own retry is the same cart - so the signature sorts.
    /// </summary>
    /// <param name="lines">One signature per line, written by the caller.</param>
    public static string CartSignature(IEnumerable<string> lines)
        => string.Join("|", lines.OrderBy(line => line, StringComparer.Ordinal));
}
