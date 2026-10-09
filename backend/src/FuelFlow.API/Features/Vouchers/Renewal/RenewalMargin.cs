namespace FuelFlow.Features.Vouchers.Renewal;

/// <summary>Whether a renewal collects less than the voucher it hands back costs.</summary>
public enum RenewalMarginVerdict
{
    /// <summary>No cost recorded on the voucher, so there is nothing to compare against.</summary>
    UnknownCost,

    /// <summary>The renewal collects at least what the voucher costs us.</summary>
    AtOrAboveCost,

    /// <summary>The renewal collects less than the voucher costs — money leaves on this exchange.</summary>
    BelowCost,
}

/// <summary>
/// The below-cost test for voucher renewals, in one place so the customer-side checkout and the
/// operator action cannot drift apart.
/// <para>
/// It is deliberately scoped to the REPLACE branch only. An extend keeps the same voucher and only
/// pushes its expiry out, so no asset leaves the warehouse and there is no cost to be under — the fee
/// is revenue on a fuel we already hold.
/// </para>
/// <para>
/// A replace hands the customer a DIFFERENT voucher from stock, and that is the moment money can
/// leave. But the customer's own voucher does not vanish: it comes back into our sellable pool. So
/// the test has two sides — the stock we hand over, and the voucher we take back — and both are valued
/// at what we PAID for them.
/// </para>
/// <para>
/// Testing the fee alone refused renewals that made money. A 300 fee against a 500 replacement looks
/// like a 200 loss, but the voucher coming back is worth the 500 we paid for it, so the exchange is
/// 300 + 500 against 500.
/// </para>
/// <para>
/// Note this is the voucher's purchase cost, NOT a "residual" adjusted by what the customer has paid
/// over the years. What a customer pays us is revenue; it does not change what we paid a supplier, so
/// <c>cost_per_liter</c> is a fact that only supplier-side events (an exchange and its surcharge,
/// see <c>VoucherCosting.AfterExchange</c>) ever move.
/// </para>
/// </summary>
public static class RenewalMargin
{
    /// <param name="amountCollectedUah">What the customer pays for this renewal.</param>
    /// <param name="liters">The voucher's nominal.</param>
    /// <param name="costPerLiter">
    /// What a litre of the replacement costs us, when known. Prefer the cost of the voucher that will
    /// actually be handed over: two vouchers of one fuel can legitimately carry different costs
    /// (bought on different terms), so an average of the pool describes neither of them.
    /// </param>
    /// <param name="incomingCostPerLiter">
    /// What a litre of the voucher the customer gives back cost us, when known. A missing value credits
    /// nothing — the conservative reading, since assuming the value of an asset we cannot price is how
    /// a loss hides.
    /// </param>
    public static RenewalMarginVerdict ForReplacement(
        decimal amountCollectedUah,
        decimal liters,
        decimal? costPerLiter,
        decimal? incomingCostPerLiter = null)
    {
        // A cost of 0 (or missing) means "never recorded", not "free". Reading it as free would make
        // every uncosted stock look like a windfall and hide a real loss once the cost is filled in.
        if (costPerLiter is not { } cost || cost <= 0m || liters <= 0m)
        {
            return RenewalMarginVerdict.UnknownCost;
        }

        var credit = incomingCostPerLiter is { } incoming && incoming > 0m ? incoming * liters : 0m;

        return amountCollectedUah + credit < cost * liters
            ? RenewalMarginVerdict.BelowCost
            : RenewalMarginVerdict.AtOrAboveCost;
    }

    /// <summary>
    /// UAH short of cost for a below-cost replace, or 0 when there is no loss. What the alert and the
    /// operator-facing message quote, so the number they are shown is the number that was computed.
    /// </summary>
    public static decimal ShortfallUah(
        decimal amountCollectedUah,
        decimal liters,
        decimal? costPerLiter,
        decimal? incomingCostPerLiter = null)
    {
        if (costPerLiter is not { } cost || cost <= 0m || liters <= 0m)
            return 0m;

        var credit = incomingCostPerLiter is { } incoming && incoming > 0m ? incoming * liters : 0m;
        return Math.Max(0m, (cost * liters) - amountCollectedUah - credit);
    }
}