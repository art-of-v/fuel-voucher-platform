using System.Globalization;

namespace FuelFlow.Features.Ledger;

/// <summary>
/// Splits a VAT-inclusive amount into its VAT and its net.
/// <para>
/// FuelFlow sells at the price on the pump, and that price includes VAT (<b>confirmed by the
/// business, 2026-10-19</b>). So a payment of 120.00 UAH carries 20.00 of VAT and 100.00 of net
/// revenue - the VAT is *inside* the amount, not added on top, which is the opposite of the usual
/// "net plus VAT" assumption and the single most common way a book like this gets its first entry
/// wrong.
/// </para>
/// <para>
/// The rate is a parameter, not a constant, for two reasons. Ukraine has not always charged the
/// standard rate on fuel, and a rate baked into a ledger class is a rate nobody can change without a
/// deploy. Callers pass what the business is registered for.
/// </para>
/// </remarks>
public static class VatCalculator
{
    /// <summary>Ukraine's standard VAT rate.</summary>
    public const decimal StandardRate = 20m;

    /// <summary>
    /// The VAT contained in <paramref name="grossAmountUah"/>, rounded to kopecks.
    /// <para>
    /// Rounds half away from zero, matching how an invoice rounds: <c>120.05</c> at 20% is 20.01 of
    /// VAT, not 20.01 of a truncated figure. Rounding the net instead would leave a kopeck that
    /// balances against nothing.
    /// </para>
    /// </summary>
    public static decimal VatFromGross(decimal grossAmountUah, decimal ratePercent = StandardRate)
    {
        if (ratePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(ratePercent), ratePercent, "A VAT rate cannot be negative.");

        if (grossAmountUah < 0)
            throw new ArgumentOutOfRangeException(
                nameof(grossAmountUah), grossAmountUah, "A gross amount cannot be negative.");

        // VAT = gross * rate / (100 + rate). Written as a division rather than a percentage of the
        // gross, because VAT is a part of the gross, not a share of it.
        var vat = grossAmountUah * ratePercent / (100m + ratePercent);

        return Math.Round(vat, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The net revenue inside <paramref name="grossAmountUah"/>. Derived by subtraction rather than
    /// by its own formula, so net + VAT always reconstructs the gross exactly - two independently
    /// rounded halves can differ from the original by a kopeck, and a trial balance that is a kopeck
    /// out is a trial balance nobody trusts.
    /// </summary>
    public static decimal NetFromGross(decimal grossAmountUah, decimal ratePercent = StandardRate)
        => Math.Round(grossAmountUah - VatFromGross(grossAmountUah, ratePercent), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The VAT and the net as a pair, which is what a posting actually needs. Preferred over calling
    /// the two methods separately so a caller cannot accidentally combine a VAT from one amount with
    /// a net from another.
    /// </summary>
    public static (decimal NetUah, decimal VatUah) Split(decimal grossAmountUah, decimal ratePercent = StandardRate)
    {
        var vat = VatFromGross(grossAmountUah, ratePercent);
        return (Math.Round(grossAmountUah - vat, 2, MidpointRounding.AwayFromZero), vat);
    }

    /// <summary>The VAT rate the business is registered for, for display. Never used in a sum.</summary>
    public static string Describe(decimal ratePercent = StandardRate)
        => string.Create(CultureInfo.InvariantCulture, $"{ratePercent.ToString("0.##", CultureInfo.InvariantCulture)}%");
}