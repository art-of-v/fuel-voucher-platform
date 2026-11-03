using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.RefundOrder;

/// <summary>
/// Says out loud when the money a refund returned and the order it belongs to stop agreeing.
/// </summary>
/// <remarks>
/// <para>
/// A refund row carries the figure Monobank was asked to move, and that figure is not ours to change
/// once the request is at the acquirer: the confirmation comes back against it, and the journal books
/// under the refund's id, which the posting engine accepts exactly once. Until #856 the repeat press
/// recomputed the row instead, so a delivery that moved after the money went back rewrote a banked
/// fact — the panel then reported "Completed" for a sum no bank ever returned, while the ledger went
/// on holding the real one.
/// </para>
/// <para>
/// The rewrite is gone. What stays is the honest half: the delivery state does keep moving after a
/// refund lands — the reconciliation job releases a delivery, a retry hands over the unit that was
/// already paid back — and when that leaves the order's undelivered value different from the sum
/// returned, a person has to decide what the difference means. Refund the rest by hand, take the
/// voucher back, post a correction: none of those is this code's call. It writes the two figures and
/// whether the books hold anything for this refund at all, and touches no money, no status, no entry
/// (planning #854, #856).
/// </para>
/// </remarks>
public static class RefundAmountDisagreement
{
    /// <summary>
    /// The audit event a repeat refund press writes when the order and the returned sum part.
    /// </summary>
    public const string EventName = "RefundAmountDisagreesWithOrder";

    /// <summary>
    /// Records the disagreement between a refund the acquirer has taken and the value its order shows.
    /// </summary>
    /// <remarks>
    /// Once per press, not once per drift: the two figures stay apart until a person closes the gap, so
    /// every press that sees them apart is worth a row. A repeated press is an operator asking the
    /// platform something it cannot answer, and an audit log is where that belongs.
    /// </remarks>
    public static async Task RecordAsync(
        ApplicationDbContext context,
        AuditEventService auditEvents,
        Payment refund,
        Order order,
        decimal undeliveredValueKopecks,
        Guid? changedByUserId,
        string? changedByUserName,
        CancellationToken cancellationToken)
    {
        var undeliveredUah = Money.FromKopecks(undeliveredValueKopecks);

        // Read off the books rather than derived from the refund, because the two disagree for a reason
        // the refund row cannot tell: every refund confirmed before #854 has no entry under its own key
        // at all, and that is the finding — the money went back and the ledger does not say so. Once a
        // refund is booked its row never moves again, so an entry that exists is an entry that matches.
        var bookedUah = await context.JournalEntries
            .AsNoTracking()
            .Where(e => e.SourceEvent == RefundJournalPoster.RefundedEvent
                     && e.SourceType == JournalSourceType.Refund
                     && e.SourceId == refund.Id
                     && e.Status == JournalEntryStatus.Posted)
            .SelectMany(e => e.Lines)
            .SumAsync(l => l.DebitUah, cancellationToken);

        // These figures are read at the period close, so they print with a dot in every locale.
        var summary =
            FormattableString.Invariant(
                $"The bank returned {refund.AmountUah:0.00} UAH for order {order.Id},")
            + FormattableString.Invariant(
                $" and the order now shows {undeliveredUah:0.00} UAH still undelivered,")
            + FormattableString.Invariant(
                $" a difference of {undeliveredUah - refund.AmountUah:0.00} UAH.")
            + " The row is not rewritten and no further cancel goes to the acquirer from here;"
            + " a person decides what the difference is worth."
            + (bookedUah == 0m
                ? $" Nothing is booked for this refund on {RefundJournalPoster.RefundedEvent};"
                    + " either it predates the posting path or it never landed."
                : string.Empty);

        auditEvents.RecordEvent(
            aggregateType: "Refund",
            aggregateId: refund.Id.ToString(),
            eventType: EventName,
            oldValue: JsonSerializer.Serialize(new { refundedUah = refund.AmountUah, bookedUah }),
            newValue: JsonSerializer.Serialize(new { undeliveredUah }),
            changedByUserId: changedByUserId ?? Guid.Empty,
            changedByUserName: changedByUserName,
            summary: summary,
            providerId: order.LineItems.FirstOrDefault()?.Brand ?? "unknown");

        await context.SaveChangesAsync(cancellationToken);
    }
}
