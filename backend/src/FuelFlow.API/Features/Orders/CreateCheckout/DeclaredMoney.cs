namespace FuelFlow.Features.Orders.CreateCheckout;

using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;

/// <summary>
/// What the held balance says about a price, on the route where the balance is the only money there is.
/// </summary>
/// <remarks>
/// <para>
/// The ruling of 2026-11-06 (planning #889) splits one question into three answers, and the difference
/// between them is the whole of what a customer is told. Held money <b>covers</b> the order: the settle
/// pays it and the acquirer is not asked. The account holds <b>nothing</b>: the order is written and
/// waits for a declaration that has not arrived — that is the branch the second button exists for.
/// Money is held and it does <b>not cover</b>: the order cannot be performed, so the tap is refused with
/// the figure named (item 5) and no half-paid order is left behind.
/// </para>
/// <para>
/// The middle branch is why this is a classifier and not a boolean. Before it, a balance of 400 ₴ and a
/// balance of nothing both answered "the order waits", because the balance was only ever asked whether it
/// covered the price. This asks the same rows the settle asks — <see cref="PaymentBalance"/>'s oldest-first
/// held money and <see cref="PaymentBalance.DrawAgainst"/>'s plan over it — so a customer is never told a
/// figure that the draw itself would not have agreed with.
/// </para>
/// <para>
/// It reads and never draws. The settle asked after the order is written stays the authority on whether
/// money moves, and it is the one that loses a race honestly: a draw the database refuses costs nothing
/// and says so. What this file decides is only whether the tap is allowed to write anything at all.
/// </para>
/// </remarks>
public static class DeclaredMoney
{
    /// <summary>The three answers the ruling allows, as something a caller branches on.</summary>
    public enum Verdict
    {
        /// <summary>The account holds nothing. Write the order and let a future declaration pay for it.</summary>
        NothingHeld,

        /// <summary>The held money covers what the order owes. The settle will pay it.</summary>
        Covers,

        /// <summary>Money is held and it is not enough. Refuse, and name the shortfall.</summary>
        PartlyFunded,
    }

    /// <param name="HeldUah">Everything held for the account, after earlier draws.</param>
    /// <param name="OwedUah">What the order needs — its price, less whatever is already on it.</param>
    /// <param name="ShortfallUah">What the plan could not draw, from the plan itself.</param>
    public sealed record Answer(decimal HeldUah, decimal OwedUah, decimal ShortfallUah)
    {
        /// <summary>
        /// A free order owes nothing, so every balance "covers" it — and the settle still refuses to
        /// draw on it, because the receipt is booked as the order's price and the journal takes no leg
        /// of 0 ₴ (planning #889, item 7). It is neither a refusal nor a payment here; it is the wait.
        /// </summary>
        public bool IsFree => OwedUah <= 0m;

        public Verdict Verdict => IsFree
            ? Verdict.NothingHeld
            : HeldUah <= 0m
                ? Verdict.NothingHeld
                : ShortfallUah > 0m
                    ? Verdict.PartlyFunded
                    : Verdict.Covers;
    }

    /// <summary>
    /// Ask the balance about this price, for the order the tap answers with — a new one, or the one that
    /// already holds the basket.
    /// </summary>
    public static async Task<Answer> InspectAsync(
        ApplicationDbContext context,
        Guid userId,
        decimal price,
        Guid? orderId,
        CancellationToken cancellationToken)
    {
        var held = await PaymentBalance.AvailableAsync(context, userId, cancellationToken);
        var onIt = orderId is { } theOrder
            ? await OrderPayment.MoneyOnItAsync(context, theOrder, cancellationToken)
            : 0m;

        // The same rule the settle plans with, asked of the same rows, so the figure a customer is given
        // is the figure a draw would have produced.
        var owed = Math.Max(0m, price - onIt);
        var plan = PaymentBalance.DrawAgainst(held, owed);

        return new Answer(held.Sum(h => h.RemainingUah), owed, plan.ShortfallUah);
    }

    /// <summary>
    /// Refuse the tap the ruling says cannot be performed. Everything else returns: nothing held is the
    /// wait, and a covering balance is the settle's to spend.
    /// </summary>
    public static void RefuseIfPartlyFunded(Answer money)
    {
        if (money.Verdict != Verdict.PartlyFunded) return;

        throw new PromoBalanceShortException(money);
    }

    /// <summary>
    /// Whether a tap that found its cart already holding a declared order should spend the balance on it.
    /// It should whenever the money covers: a declaration that arrived after the order is something the
    /// arrival side only names when the customer has exactly one open order at exactly that price
    /// (planning #889, item 4), and the customer's own tap is the one request that always knows which
    /// order it means.
    /// </summary>
    /// <remarks>
    /// An order that owes nothing answers the same way, and that is not the same case: it is one with
    /// money already named on it by the channel, whose only missing step is the settle — which the
    /// settle handler completes without a draw, because the money is on the order and counted whole
    /// there. Refusing to ask for it would leave the customer tapping over an order the company has
    /// already been paid for, and told it is still waiting.
    /// </remarks>
    public static bool ShouldSettleAReusedOrder(Answer money)
        => money.Verdict == Verdict.Covers || money.IsFree;

    /// <summary>
    /// The money question of a tap that is being answered with the order that already holds the basket:
    /// refuse it if the balance is only part of the price, spend the balance on it if the balance is the
    /// whole price, and otherwise leave the order waiting for the declaration that pays it.
    /// </summary>
    /// <returns>True when the balance paid for the order, which is the answer the caller returns.</returns>
    public static async Task<bool> SpendOnReusedOrderAsync(
        ApplicationDbContext context,
        SettleFromHeldMoneyCommandHandler settle,
        Guid userId,
        Order reused,
        CancellationToken cancellationToken)
    {
        var money = await InspectAsync(context, userId, reused.Price, reused.Id, cancellationToken);

        RefuseIfPartlyFunded(money);

        if (!ShouldSettleAReusedOrder(money)) return false;

        var paid = await settle.HandleAsync(
            new SettleFromHeldMoneyCommand
            {
                OrderId = reused.Id,
                Because = "the promo tap on the order that holds this cart"
            },
            cancellationToken);

        return paid.Settled;
    }
}
