namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// A checkout was refused because the basket already holds an order that money may be moving on, and
/// the request asked for a different way of paying than that order was written on. Surfaces as HTTP
/// 409 with a stable <see cref="Code"/> - the same shape as the other checkout refusals.
/// </summary>
/// <remarks>
/// <para>
/// This is the one cell of the two-button flow (planning #889) where the platform refuses to choose
/// for the customer. The order in the way was created seconds or minutes ago, so its age does not
/// tell anyone whether it is finished: a bank page may be open on another device, or the finance
/// channel may have just named a declaration on it. Both are a transaction in progress, and the two
/// ways out of it - pay it, or give it up - are the customer's, not a retry's.
/// </para>
/// <para>
/// The message says what is true without claiming to know which of the two it is. What the customer
/// does next (finish that order, or delete it in the app and tap again) works either way.
/// </para>
/// </remarks>
public sealed class CheckoutAlreadyInPaymentException : Exception
{
    public const string Code = "checkout_in_payment";

    /// <summary>The order standing in the way, for logs and telemetry and never for the message.</summary>
    public Guid OrderId { get; }

    public CheckoutAlreadyInPaymentException(Guid orderId)
        : base("This fuel is already in an order that is being paid. Pay that order, or delete it and try again.")
    {
        OrderId = orderId;
    }
}
