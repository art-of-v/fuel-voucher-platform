using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Ledger;

/// <summary>
/// What the receipt carved out of one order's payment, and how much of it is still on the liability.
/// </summary>
/// <remarks>
/// <para>
/// Two corrections take VAT off 643 for one order: the refund, which releases the carve of the money
/// that came back, and the perimeter release, which releases the carve of the part delivered on the
/// outside route. Both are computed from a share of the gross with the same formula the receipt used,
/// and both are booked as facts of their own. The problem that shape has is arithmetic: the receipt
/// divides once, on the whole payment, and two divisions of the halves can round one kopeck past it -
/// 1 724.00 ₴ carves 287.33, while two 862.00 ₴ halves each carve 143.67.
/// </para>
/// <para>
/// So the last correction takes what is left, not what its own division says. This reads the figures
/// it needs from the books rather than recomputing them: the carve is the receipt's own entry, the
/// account is the one that entry credited, and what was already taken is every posted debit on that
/// account attributable to the order - the release keyed on the order itself and the refunds of it.
/// That is also what stops a correction from releasing VAT the receipt never asserted: an order with
/// no carve (a promo sale, or a receipt that was never booked) has nothing here to take.
/// </para>
/// <para>
/// Deliberately not a per-order subledger. The period close reads accounts, and an attribution that
/// disagreed with the postings would be a second set of books; this only answers "how much of this
/// order's carve is still standing", and the caller decides whether that is enough for its fact.
/// The refund and the perimeter release are two features, so this is public on purpose rather than
/// duplicated: a second copy of the cap is how the 643 balance stops closing at zero.
/// </para>
/// </remarks>
public static class CarvedVat
{
    /// <summary>The event whose entry is the carve - the one <c>PostReceiptToJournalAsync</c> writes.</summary>
    public const string ReceiptEvent = "payment.received.vat";

    /// <summary>
    /// The amount a posted entry under <paramref name="sourceId"/> debits on its first line, or zero
    /// when there is no such entry. Both the refund and the release put the correction on line 1.
    /// </summary>
    public static async Task<decimal> PostedAmountAsync(
        ApplicationDbContext context,
        string sourceEvent,
        JournalSourceType sourceType,
        Guid sourceId,
        CancellationToken cancellationToken)
        => await (from line in context.JournalLines.AsNoTracking()
                  join entry in context.JournalEntries.AsNoTracking() on line.EntryId equals entry.Id
                  where entry.SourceEvent == sourceEvent
                        && entry.SourceType == sourceType
                        && entry.SourceId == sourceId
                        && line.LineNo == 1
                  select line.DebitUah)
                 .SumAsync(cancellationToken);

    /// <summary>
    /// The order's carve and what has already come off it. Both zero when the receipt booked no VAT.
    /// </summary>
    public static async Task<Carve> ForOrderAsync(
        ApplicationDbContext context,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var carveLines = await (
            from line in context.JournalLines.AsNoTracking()
            join entry in context.JournalEntries.AsNoTracking() on line.EntryId equals entry.Id
            where entry.SourceEvent == ReceiptEvent && entry.SourceId == orderId
            select new { line.LineNo, line.AccountId, line.DebitUah, line.CreditUah })
            .ToListAsync(cancellationToken);

        // The engine puts the debit on line 1 and the liability on line 2 of every posting, so the
        // carve's amount and the account it sits on are both read off the entry itself. No account
        // code is named here: the chart is the accountant's decision, not this class's.
        var carved = carveLines.Where(l => l.LineNo == 1).Sum(l => l.DebitUah);
        var liabilityAccountId = carveLines
            .FirstOrDefault(l => l.CreditUah > 0m)?.AccountId;

        if (carved <= 0m || liabilityAccountId is not { } accountId)
            return Carve.Nothing;

        // A refund is keyed on its own payment row, not on the order, so the order's refunds are the
        // other half of what "belongs to this order" means.
        var refundIds = await context.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Kind == PaymentKind.Refund)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        // Drafts are not in the books and cannot have taken anything off the liability. The carve's
        // own credit line is excluded by its event name, or it would subtract the whole carve from
        // itself.
        var taken = await (
            from line in context.JournalLines.AsNoTracking()
            join entry in context.JournalEntries.AsNoTracking() on line.EntryId equals entry.Id
            where line.AccountId == accountId
                  && entry.SourceEvent != ReceiptEvent
                  && entry.Status != JournalEntryStatus.Draft
                  && (entry.SourceId == orderId
                      || (entry.SourceId != null && refundIds.Contains(entry.SourceId.Value)))
            select line.DebitUah - line.CreditUah)
            .SumAsync(cancellationToken);

        return new Carve(carved, taken);
    }
}

/// <summary>
/// The carve of one order and the part of it already released. <see cref="RemainingUah"/> is the
/// ceiling a new correction may take, and the last one to run closes the account to zero.
/// </summary>
public sealed record Carve(decimal CarvedUah, decimal TakenUah)
{
    public static readonly Carve Nothing = new(0m, 0m);

    /// <summary>What is still on the liability. Never negative in a book this code wrote it into.</summary>
    public decimal RemainingUah => Math.Max(0m, CarvedUah - TakenUah);
}
