namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A fuel nobody has priced yet: the catalogue publishes its pump price, but no supplier cost and
/// no margin are recorded, so there is no customer price to compute and nothing to charge.
/// </summary>
/// <remarks>
/// <para>
/// A state, not a fault. The storefront lists such a fuel at its pump price and marks it
/// unavailable, and the buy path refuses it - so a request that meets one is a customer asking for
/// something the catalogue does not sell yet. It is therefore a business state that repeats
/// itself, and the error journal's noise filter keeps it out of <c>error_logs</c>: a row per
/// attempt is what buried the real failures in there (the same lesson as the below-cost import
/// that used to write 689 rows a day).
/// </para>
/// <para>
/// It derives from <see cref="InvalidOperationException"/> so the paths that answer a caller with
/// "the request could not be processed" keep doing so without knowing about a new type, and so the
/// refusal stays a 400 rather than becoming a 500.
/// </para>
/// </remarks>
public sealed class NoPriceAvailableException : InvalidOperationException
{
    public NoPriceAvailableException(string message)
        : base(message)
    {
    }
}
