namespace FuelFlow.Features.Vouchers.Renewal;

/// <summary>
/// Pure money maths for voucher renewal/replacement. The price of renewing one voucher is
/// <c>round(litres × UAH-per-litre(term), 2 dp)</c> — it scales linearly with the voucher's nominal
/// (its litres), and the per-litre rate is the manager-set tier rate. A batch invoice is the sum of
/// its per-voucher line amounts, each carrying its own term. Amounts are decimal UAH to the kopeck
/// (spec §12, #90; rounds half away from zero), matching the Order/line-item price columns.
/// </summary>
public static class VoucherRenewalPricing
{
    /// <summary>Renewal price for a single voucher, in decimal UAH to 2 dp (rounds half away from zero).</summary>
    /// <exception cref="ArgumentOutOfRangeException">If litres or the rate is negative.</exception>
    public static decimal LineAmountUah(decimal liters, decimal ratePerLiterUah)
    {
        if (liters < 0m)
            throw new ArgumentOutOfRangeException(nameof(liters), liters, "Litres must not be negative.");
        if (ratePerLiterUah < 0m)
            throw new ArgumentOutOfRangeException(nameof(ratePerLiterUah), ratePerLiterUah, "Rate per litre must not be negative.");

        return Math.Round(liters * ratePerLiterUah, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Total for a batch: the sum of each voucher's <see cref="LineAmountUah"/>, each line with its
    /// own term rate.
    /// </summary>
    public static decimal TotalUah(IEnumerable<(decimal Liters, decimal RatePerLiterUah)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Sum(line => LineAmountUah(line.Liters, line.RatePerLiterUah));
    }
}
