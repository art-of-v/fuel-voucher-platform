namespace FuelFlow.Features.Vouchers.Renewal;

/// <summary>
/// Pure money maths for voucher renewal/replacement. The price of renewing one voucher is
/// <c>round(litres × UAH-per-litre(term))</c> — it scales linearly with the voucher's nominal (its
/// litres), and the per-litre rate is the manager-set tier rate. A batch invoice is the sum of its
/// per-voucher line amounts, each carrying its own term. Amounts are whole UAH (kopecks deferred,
/// Option A), matching the Order/line-item price columns.
/// </summary>
public static class VoucherRenewalPricing
{
    /// <summary>Renewal price for a single voucher, in whole UAH (rounds half away from zero).</summary>
    /// <exception cref="ArgumentOutOfRangeException">If litres or the rate is negative.</exception>
    public static int LineAmountUah(decimal liters, decimal ratePerLiterUah)
    {
        if (liters < 0m)
            throw new ArgumentOutOfRangeException(nameof(liters), liters, "Litres must not be negative.");
        if (ratePerLiterUah < 0m)
            throw new ArgumentOutOfRangeException(nameof(ratePerLiterUah), ratePerLiterUah, "Rate per litre must not be negative.");

        return (int)Math.Round(liters * ratePerLiterUah, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Total for a batch: the sum of each voucher's <see cref="LineAmountUah"/>, each line with its
    /// own term rate. Throws <see cref="OverflowException"/> if the sum exceeds <see cref="int"/>.
    /// </summary>
    public static int TotalUah(IEnumerable<(decimal Liters, decimal RatePerLiterUah)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Sum(line => LineAmountUah(line.Liters, line.RatePerLiterUah));
    }
}
