namespace FuelFlow.Features.Payments.Admin.DismissPayment;

/// <summary>
/// Takes money we hold off the worklist, stating why. The money itself is not moved: the dismissal
/// answers "we have seen this and it will never be placed", which is the one answer the queue had no
/// way to record.
/// </summary>
/// <param name="PaymentId">The payment leaving the worklist.</param>
/// <param name="Reason">Why it can never be placed. Required — a dismissal nobody can explain is worse than the row.</param>
/// <param name="ActorId">Who dismissed it. The claims are the controller's to read.</param>
/// <param name="ActorName">The name the audit row carries.</param>
public sealed record DismissPaymentCommand(
    Guid PaymentId,
    string? Reason,
    Guid? ActorId,
    string? ActorName);

/// <summary>The answer to a dismissal: was the payment found, was it dismissed, and when not — why not.</summary>
public sealed record DismissPaymentResult(bool Found, bool Success, string? Error)
{
    public static DismissPaymentResult NotFound() => new(false, false, "Payment not found");

    public static DismissPaymentResult Refused(string error) => new(true, false, error);

    public static DismissPaymentResult Done() => new(true, true, null);
}
