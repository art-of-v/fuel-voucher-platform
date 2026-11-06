namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// The promotional button was pressed on an account whose held money does not cover this order, and
/// some of that money exists. Surfaces as HTTP 409 with a stable <see cref="Code"/> and the three
/// figures in its <c>data</c>, because the customer's sentence needs the amount and must not have to
/// read it out of ours.
/// </summary>
/// <remarks>
/// <para>
/// This is item 5 of the owner's ruling of 2026-11-06 (planning #889): «Якщо на рахунку не вистачає
/// коштів для покриття замовлення — воно не може бути виконане». It is refused at the tap and nothing
/// is written, so the account never holds a half-paid promotional order claiming stock, and the figure
/// is named rather than left in a log for an operator.
/// </para>
/// <para>
/// It is not <see cref="PromoAccessException"/>, which says the account is not on the offer at all, and
/// it is not the answer for an empty balance. An account holding nothing is the ruling's other branch —
/// the order is written and waits for a future declaration — and refusing that would close the door the
/// second button exists to open. The line between them is drawn by <see cref="DeclaredMoney"/>.
/// </para>
/// </remarks>
public sealed class PromoBalanceShortException : Exception
{
    public const string Code = "promo_balance_short";

    /// <summary>What the order still owed beyond everything the account holds.</summary>
    public decimal ShortfallUah { get; }

    /// <summary>Everything held for this account right now.</summary>
    public decimal HeldUah { get; }

    /// <summary>What this order needed.</summary>
    public decimal OwedUah { get; }

    public PromoBalanceShortException(DeclaredMoney.Answer money)
        : base($"This order needs {money.OwedUah:0.00} ₴ and the account holds {money.HeldUah:0.00} ₴, "
             + $"so {money.ShortfallUah:0.00} ₴ is missing. Nothing was ordered and nothing was spent.")
    {
        ShortfallUah = money.ShortfallUah;
        HeldUah = money.HeldUah;
        OwedUah = money.OwedUah;
    }
}
