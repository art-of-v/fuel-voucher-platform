namespace FuelFlow.SharedKernel;

/// <summary>
/// Currency unit conversions. The FuelFlow domain model stores money in decimal UAH
/// to the kopeck (numeric(12,2): orders.price, line-item unit prices, fuel packages,
/// fuel type prices — spec §12, #90); kopeck integers are required only at the Monobank
/// merchant API boundary (invoice creation, refunds, webhooks). Every conversion between
/// the two units must go through these helpers so unit mistakes stay greppable in one place.
/// </summary>
public static class Money
{
    public static long ToKopecks(int uah) => (long)uah * 100;

    public static long ToKopecks(long uah) => uah * 100;

    public static decimal ToKopecks(decimal uah) => uah * 100m;

    /// <summary>
    /// Decimal UAH → integer kopecks for the Monobank boundary (invoice <c>Amount</c>,
    /// webhook amount match). Rounds half away from zero so a 2-dp domain amount maps to
    /// its exact kopeck integer (e.g. 456.70 → 45670). Use this — not <see cref="ToKopecks(decimal)"/>,
    /// which keeps the decimal — wherever a <see cref="long"/> kopeck amount crosses the API line.
    /// </summary>
    public static long ToKopecksLong(decimal uah) => (long)Math.Round(uah * 100m, MidpointRounding.AwayFromZero);

    public static decimal FromKopecks(long kopecks) => kopecks / 100m;
}
