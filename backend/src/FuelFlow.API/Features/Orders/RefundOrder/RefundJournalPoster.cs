using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Orders.RefundOrder;

/// <summary>
/// Books the money that went back: one entry, the mirror of the receipt that brought it in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is called from the confirmation and not from the refund request.</b> A requested refund
/// is a promise; a cancel-list entry the acquirer reports as <c>success</c> is the fact. The capture
/// path made the same choice on 2026-10-19 - trust the settled state rather than the announcement -
/// and a refund booked at request time would put money in the books that Monobank may still refuse.
/// Both confirmation routes (the poller and the webhook) converge on one method, so this is called
/// from one place instead of two.
/// </para>
/// <para>
/// <b>Keyed on the refund payment, not on the order.</b> <c>payment.received</c> is keyed by order
/// because an order has one capture. A refund row is re-used when the operator re-requests it and its
/// amount is corrected in place, so an order-keyed entry would keep the first amount standing while
/// the money moved - and the engine's idempotency would report that as success.
/// </para>
/// <para>
/// <b>No VAT leg here.</b> <c>payment.received.vat</c> carved the VAT out when the money arrived;
/// whether a returned advance releases that liability in the same period is a decision the chart has
/// no event for (there is no <c>payment.refunded.vat</c> mapping), so it is not this code's to make.
/// Planning #854.
/// </para>
/// </remarks>
public sealed class RefundJournalPoster
{
    /// <summary>The event every booked refund carries; it is the key an accountant queries.</summary>
    public const string RefundedEvent = "payment.refunded";

    private readonly JournalPostingService _journal;
    private readonly ILogger<RefundJournalPoster> _logger;

    public RefundJournalPoster(JournalPostingService journal, ILogger<RefundJournalPoster> logger)
    {
        _journal = journal;
        _logger = logger;
    }

    /// <summary>
    /// Posts the refund's debit-credit pair. Idempotent on
    /// <c>(payment.refunded, Refund, refund.Id)</c>, so a retried sync or a replayed webhook books it
    /// once.
    /// </summary>
    /// <exception cref="LedgerUnmappedEventException">
    /// The chart has no rule for <see cref="RefundedEvent"/> - which is a hole in the books, not a
    /// reason to let the money stay unrecorded silently. The caller logs it.
    /// </exception>
    /// <exception cref="LedgerPeriodClosedException">The period holding today is closed.</exception>
    public async Task<JournalEntry?> BookConfirmedRefundAsync(
        Payment refund,
        Order order,
        CancellationToken cancellationToken = default)
    {
        if (refund.Kind != PaymentKind.Refund)
            throw new LedgerPostingException(
                $"Refund {refund.Id} is a {refund.Kind}; only a Refund payment may book '{RefundedEvent}'.");

        if (refund.AmountUah <= 0m)
        {
            // A zero refund is not a posting: PostAsync would throw on it, and nothing was returned.
            _logger.LogInformation(
                "REFUND LEDGER: refund {RefundId} for order {OrderId} is {Amount}; nothing to book.",
                refund.Id, order.Id, refund.AmountUah);
            return null;
        }

        var entry = await _journal.PostAsync(
            new PostJournalEntryRequest
            {
                SourceEvent = RefundedEvent,
                SourceType = JournalSourceType.Refund,
                SourceId = refund.Id,
                // The day the acquirer returned the money, not the day a sync happened to look. A
                // refund confirmed days ago belongs in the period the money moved; if that period is
                // closed the posting says so instead of quietly dating the entry elsewhere.
                EntryDate = DateOnly.FromDateTime(refund.PaidAtUtc ?? SystemClock.UtcNow),
                AmountUah = refund.AmountUah,
                Description = $"Повернення за замовленням {order.Id}",
                // The operator who ordered the refund when there was one; the customer's own order
                // user otherwise, which is what payment.received already does for the receipt.
                PostedByUserId = refund.CreatedByUserId ?? refund.UserId ?? order.UserId,
            },
            cancellationToken);

        _logger.LogInformation(
            "REFUND LEDGER: booked {Amount} UAH for refund {RefundId} (order {OrderId}) as entry {EntryNo}.",
            refund.AmountUah, refund.Id, order.Id, entry.EntryNo);

        return entry;
    }
}
