namespace FuelFlow.Features.Auth.SendCode.Services;

/// <summary>
/// Thrown when the daily outbound SMS budget is spent. Distinct from a transport failure so the
/// caller can answer 503 rather than retrying into an empty budget.
/// </summary>
public sealed class SmsBudgetExhaustedException : Exception
{
    public SmsBudgetExhaustedException()
        : base("Outbound SMS budget exhausted")
    {
    }
}
