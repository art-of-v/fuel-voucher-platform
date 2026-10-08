namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// How a voucher's own cost (UAH per liter) moves through its life. The cost lives on the voucher rather
/// than on the import batch, because vouchers of one brand do not cost the same — they are bought on
/// different terms, and each is reduced independently when a customer pays to extend it.
/// </summary>
public static class VoucherCosting
{
    /// <summary>
    /// Cost of one voucher after a customer paid <paramref name="amountPaid"/> to extend it.
    /// </summary>
    /// <remarks>
    /// The payment buys time out of the voucher's remaining life, so the fuel we own is worth that much
    /// less: <c>cost − paid/liters</c>, floored at zero. Flooring matters because a customer can pay more than
    /// the voucher's whole original cost on a cheap voucher, and a negative cost would then drag the blended
    /// price of the whole fuel down below what we actually pay any supplier.
    /// </remarks>
    public static decimal AfterCustomerPayment(decimal costPerLiter, decimal liters, decimal amountPaid)
    {
        if (liters <= 0m) return costPerLiter;
        if (amountPaid <= 0m) return costPerLiter;

        var reduced = costPerLiter - amountPaid / liters;
        return reduced < 0m ? 0m : reduced;
    }

    /// <summary>
    /// Cost of the replacement voucher that <paramref name="oldCostPerLiter"/> was exchanged for.
    /// </summary>
    /// <remarks>
    /// An exchange moves paper, it does not create value: the new voucher is the old one plus the доплата we
    /// paid the provider. Spreading the lump sum evenly over the new liters matches what was actually paid and
    /// keeps the pool conserved — <c>Σ old + surcharge = Σ new</c> — so a reprice after an exchange moves the
    /// blended price by exactly the surcharge and nothing else.
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