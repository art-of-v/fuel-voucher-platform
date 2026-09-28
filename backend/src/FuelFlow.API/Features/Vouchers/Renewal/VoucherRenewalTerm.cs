namespace FuelFlow.Features.Vouchers.Renewal;

/// <summary>
/// The fixed ladder of renewal/replacement terms a customer may buy for a voucher. Exactly eight
/// tiers, from one week to six months; the manager enables a subset and sets a UAH-per-litre rate
/// for each in the admin panel (shorter term ⇒ lower rate ⇒ bigger discount).
/// </summary>
public enum VoucherRenewalTerm
{
    OneWeek,
    TwoWeeks,
    OneMonth,
    TwoMonths,
    ThreeMonths,
    FourMonths,
    FiveMonths,
    SixMonths
}

public static class VoucherRenewalTerms
{
    /// <summary>All eight tiers in ascending-duration order.</summary>
    public static readonly IReadOnlyList<VoucherRenewalTerm> All = new[]
    {
        VoucherRenewalTerm.OneWeek,
        VoucherRenewalTerm.TwoWeeks,
        VoucherRenewalTerm.OneMonth,
        VoucherRenewalTerm.TwoMonths,
        VoucherRenewalTerm.ThreeMonths,
        VoucherRenewalTerm.FourMonths,
        VoucherRenewalTerm.FiveMonths,
        VoucherRenewalTerm.SixMonths
    };

    /// <summary>
    /// Stable short code used in <c>app_settings</c> keys and API/DTO payloads. Never localise or
    /// renumber these — persisted setting rows and client requests key off them.
    /// </summary>
    public static string Code(this VoucherRenewalTerm term) => term switch
    {
        VoucherRenewalTerm.OneWeek => "1w",
        VoucherRenewalTerm.TwoWeeks => "2w",
        VoucherRenewalTerm.OneMonth => "1m",
        VoucherRenewalTerm.TwoMonths => "2m",
        VoucherRenewalTerm.ThreeMonths => "3m",
        VoucherRenewalTerm.FourMonths => "4m",
        VoucherRenewalTerm.FiveMonths => "5m",
        VoucherRenewalTerm.SixMonths => "6m",
        _ => throw new ArgumentOutOfRangeException(nameof(term), term, null)
    };

    /// <summary>
    /// Advances <paramref name="from"/> by this term. Week tiers add calendar days (7/14); month
    /// tiers add calendar months (day-of-month preserved, clamped by <see cref="DateOnly.AddMonths"/>).
    /// This is the single primitive behind both branches: extend = ApplyTo(old expiry), replace =
    /// stock must reach at least ApplyTo(today).
    /// </summary>
    public static DateOnly ApplyTo(this VoucherRenewalTerm term, DateOnly from) => term switch
    {
        VoucherRenewalTerm.OneWeek => from.AddDays(7),
        VoucherRenewalTerm.TwoWeeks => from.AddDays(14),
        VoucherRenewalTerm.OneMonth => from.AddMonths(1),
        VoucherRenewalTerm.TwoMonths => from.AddMonths(2),
        VoucherRenewalTerm.ThreeMonths => from.AddMonths(3),
        VoucherRenewalTerm.FourMonths => from.AddMonths(4),
        VoucherRenewalTerm.FiveMonths => from.AddMonths(5),
        VoucherRenewalTerm.SixMonths => from.AddMonths(6),
        _ => throw new ArgumentOutOfRangeException(nameof(term), term, null)
    };

    /// <summary>Parses a <see cref="Code"/> back to its term. Returns false for anything unknown.</summary>
    public static bool TryFromCode(string? code, out VoucherRenewalTerm term)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Code(), code, StringComparison.OrdinalIgnoreCase))
            {
                term = candidate;
                return true;
            }
        }

        term = default;
        return false;
    }
}
