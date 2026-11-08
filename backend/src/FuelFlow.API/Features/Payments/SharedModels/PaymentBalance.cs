namespace FuelFlow.Payments.SharedModels;

using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// What a customer's held money is, and how a wanted amount is split over it.
/// </summary>
/// <remarks>
/// <para>
/// The balance is not a column. It is what a set of declared transfers adds up to once each has given
/// back what earlier orders already took, and the two ways it is read — how much is free, and which
/// transfer pays for this order — are what this class owns. Keeping them here is what stops a caller
/// writing <c>AmountUah</c> and calling it the money the customer has, which is true only of a transfer
/// nobody has spent yet and is the mistake this whole slice replaces.
/// </para>
/// <para>
/// Only <b>held</b> money appears: a succeeded capture, naming this customer, naming no order, and not
/// dismissed from the worklist. A payment already attached to an order is that order's money in full
/// and is not in anyone's balance, and the database refuses a draw on it — see
/// <see cref="PaymentAllocation"/>. A dismissal is the operator's answer that this money will never be
/// placed on a sale, so spending it automatically would reverse a decision a person made on purpose —
/// the shape a test capture on a QA account would otherwise take. A refund is not money the customer
/// holds; it is money going the other way, and it is excluded by kind rather than by judgement.
/// </para>
/// </remarks>
public static class PaymentBalance
{
    /// <summary>One held payment and what is still free on it.</summary>
    /// <param name="PaymentId">The money row.</param>
    /// <param name="RemainingUah">Its amount less every draw already made against it.</param>
    /// <param name="PaidAtUtc">When the money arrived — the order the draw goes through.</param>
    public sealed record Held(Guid PaymentId, decimal RemainingUah, DateTime PaidAtUtc);

    /// <summary>One line of a draw: this much of that payment.</summary>
    public sealed record Draw(Guid PaymentId, decimal AmountUah);

    /// <summary>
    /// What a wanted amount would take, or why it cannot be taken.
    /// </summary>
    /// <param name="Draws">Empty when the balance is nothing at all.</param>
    /// <param name="ShortfallUah">Zero when the draws cover the amount.</param>
    public sealed record Plan(IReadOnlyList<Draw> Draws, decimal ShortfallUah)
    {
        public bool Covers => ShortfallUah == 0m;
    }

    /// <summary>
    /// The customer's held money, oldest transfer first, each with what is left of it.
    /// </summary>
    /// <remarks>
    /// A payment fully drawn on is left out rather than reported as zero: an empty line in someone's
    /// balance is a row an operator reads as still available, and this list is what the refusals quote.
    /// </remarks>
    public static async Task<IReadOnlyList<Held>> AvailableAsync(
        ApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var held = await context.Payments
            .AsNoTracking()
            .Where(p => p.UserId == userId
                     && p.OrderId == null
                     && p.Kind == PaymentKind.Capture
                     && p.Status == PaymentStatus.Succeeded
                     // Dismissal is not a note on the worklist, it is a decision about the money: the
                     // operator looked at the transfer, said which answer it was, and closed it. A
                     // dismissed row stays in the bank and in the books, but it is not the customer's
                     // to spend any more - without this line the next checkout would quietly settle
                     // from the test capture the operator had just declared will never be placed.
                     && p.DismissedAtUtc == null)
            .OrderBy(p => p.PaidAtUtc)
            .ThenBy(p => p.Id)
            .Select(p => new Held(
                p.Id,
                p.AmountUah - p.Allocations.Sum(a => a.AmountUah),
                // The payment CHECK makes NULL impossible on a Succeeded row; the coalesce is so the
                // projection has a value, not a guard against a state that cannot arrive.
                p.PaidAtUtc ?? DateTime.MinValue))
            .ToListAsync(cancellationToken);

        return held.Where(h => h.RemainingUah > 0m).ToList();
    }

    /// <summary>
    /// Splits an amount over held payments, oldest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Oldest first because that is the only rule available that is not a choice between the
    /// customer's own money: transfers are fungible, so which one pays for which sale changes nothing
    /// about what anyone holds, and the one property worth keeping is that a transfer sitting in the
    /// account longest is the one that gets used. It also makes a retry land on the same rows, which an
    /// arbitrary pick would not.
    /// </para>
    /// <para>
    /// It answers with the shortfall instead of throwing: the figure is the answer the customer and the
    /// operator need, and "not enough" is a state of an account rather than a programming error.
    /// </para>
    /// </remarks>
    public static Plan DrawAgainst(IReadOnlyList<Held> held, decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var draws = new List<Draw>();
        var stillWanted = amount;

        foreach (var payment in held)
        {
            if (stillWanted == 0m)
            {
                break;
            }

            if (payment.RemainingUah <= 0m)
            {
                continue;
            }

            var take = Math.Min(payment.RemainingUah, stillWanted);
            draws.Add(new Draw(payment.PaymentId, take));
            stillWanted -= take;
        }

        return new Plan(draws, stillWanted);
    }
}
