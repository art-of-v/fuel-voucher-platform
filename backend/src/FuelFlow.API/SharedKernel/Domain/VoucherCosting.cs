namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// How a voucher's own cost (UAH per liter) moves through its life. The cost lives on the voucher rather
/// than on the import batch, because vouchers of one brand do not cost the same — they are bought on
/// different terms.
/// </summary>
/// <remarks>
/// <para>
/// <c>cost_per_liter</c> answers exactly one question: <b>what did we pay a supplier for this voucher</b>.
/// It is a purchase fact.
/// </para>
/// <para>
/// What a customer pays us is <b>revenue</b>, and revenue does not change what we paid. A voucher bought
/// at 50/litre still costs 50/litre after the customer has paid to extend it — the fuel is still ours to
/// deliver and the supplier invoice is still 500. This used to read differently: the code subtracted the
/// fee from the cost on a paid renewal, which made the column mean "what is this worth to us now" as
/// well. That single column fed the blended cost, the package prices and supplier invoicing, so the two
/// meanings leaked into each other.
/// </para>
/// <para>
/// <b>Valuing a voucher is a different question, and it belongs somewhere else.</b> "What is this voucher
/// worth to us" depends on how much of its life is still sellable, and that is a pricing decision
/// (margin, min discount), not a restatement of the purchase price. The renewal guard is the one place
/// that needs such a value, and it takes it from the voucher's purchase cost directly — see
/// <c>RenewalMargin</c>.
/// </para>
/// </remarks>
public static class VoucherCosting
{
    /// <summary>
    /// Cost of the replacement voucher that <paramref name="oldCostPerLiter"/> was exchanged for.
    /// </summary>
    /// <remarks>
    /// An exchange moves paper, it does not create value: the new voucher is the old one plus the доплата we
    /// paid the provider. Spreading the lump sum evenly over the new liters matches what was actually paid and
    /// keeps the pool conserved — <c>Σ old + surcharge = Σ new</c> — so a reprice after an exchange moves the
    /// blended price by exactly the surcharge and nothing else.
    /// <para>
    /// This is the one event that legitimately moves a voucher's cost, and it moves it because WE paid
    /// more — not because a customer did.
    /// </para>
    /// </remarks>
    public static decimal AfterExchange(
        decimal oldCostPerLiter,
        decimal surchargeUah,
        decimal totalNewLiters)
    {
        if (totalNewLiters <= 0m) return oldCostPerLiter;

        return oldCostPerLiter + surchargeUah / totalNewLiters;
    }
}