using FuelFlow.SharedKernel.Errors;
namespace FuelFlow.Features.Payments.Admin.AttachPayment;

/// <summary>
/// Places money that is already held: names its owner, and/or the order it settles. Both optional,
/// because the two situations are separate — an unrecognised code needs the first, a prepayment
/// waiting on an order needs the second, and most prepayments need both eventually.
/// </summary>
/// <param name="PaymentId">The payment being placed.</param>
/// <param name="OrderId">The order this money pays for, when the operator names one.</param>
/// <param name="UserId">The customer this money belongs to, when the operator names one.</param>
/// <param name="ActorId">Who is placing the money. The claims are the controller's to read; the handler is told who, not how to find out.</param>
/// <param name="ActorName">The name the audit row carries.</param>
public sealed record AttachPaymentCommand(
    Guid PaymentId,
    Guid? OrderId,
    Guid? UserId,
    Guid? ActorId,
    string? ActorName);

/// <summary>
/// The answer to a placement: was the payment found, was it placed, and — when it was not — why not.
/// </summary>
/// <param name="Found">Whether the payment exists at all.</param>
/// <param name="Success">Whether the money was placed.</param>
/// <param name="Error">Why it was not, when it was not.</param>
public sealed record AttachPaymentResult(bool Found, bool Success, string? Error)
{
    public static AttachPaymentResult NotFound() => new(false, false, "Payment not found");

    public static AttachPaymentResult Refused(string error) => new(true, false, error);

    public static AttachPaymentResult Done() => new(true, true, null);
}
