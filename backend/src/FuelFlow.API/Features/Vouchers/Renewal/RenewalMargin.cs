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
/// The below-cost test for voucher renewals, in one place so the self-serve checkout and the
/// operator action cannot drift apart.
/// <para>
/// It is deliberately scoped to the REPLACE branch only. An extend keeps the same voucher and only
/// pushes its expiry out, so no asset leaves the warehouse and there is no cost to be under — the
/// payment is credited against the voucher's residual cost instead (see
/// <c>VoucherCosting.AfterCustomerPayment</c>). A replace hands the customer a different voucher from
/// stock, and THAT is the moment money can leave: we pay for the stock voucher and collect a renewal
/// fee, and if the fee is lower than the voucher cost the difference is gone.
/// </para>
/// <para>
/// Because a replace always issues stock, this is the one revenue path where the loss is knowable at
/// the moment of the sale rather than only in hindsight.
/// </para>
/// </summary>
public static class RenewalMargin
{
    /// <param name="amountCollectedUah">What the customer pays for this renewal.</param>
    /// <param name="liters">The voucher's nominal.</param>
    /// <param name="costPerLiter">What a litre of the replacement costs us, when known.</param>
    public static RenewalMarginVerdict ForReplacement(
        decimal amountCollectedUah,
        decimal liters,
        decimal? costPerLiter)
    {
        // A cost of 0 (or missing) means "never recorded", not "free". Reading it as free would make
        // every uncosted stock look like a windfall and hide a real loss once the cost is filled in.
        if (costPerLiter is not { } cost || cost <= 0m || liters <= 0m)
        {
            return RenewalMarginVerdict.UnknownCost;
        }

        return amountCollectedUah < cost * liters
            ? RenewalMarginVerdict.BelowCost
            : RenewalMarginVerdict.AtOrAboveCost;
    }

    /// <summary>
    /// UAH short of cost for a below-cost replace, or 0 when there is no loss. What the alert and the
    /// operator-facing message quote, so the number they are shown is the number that was computed.
    /// </summary>
    public static decimal ShortfallUah(decimal amountCollectedUah, decimal liters, decimal? costPerLiter)
        => costPerLiter is { } cost && cost > 0m && liters > 0m
            ? Math.Max(0m, (cost * liters) - amountCollectedUah)
            : 0m;
}