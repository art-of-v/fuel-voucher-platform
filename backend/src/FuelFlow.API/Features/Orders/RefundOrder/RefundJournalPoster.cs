using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Orders.RefundOrder;

/// <summary>
/// Books the money that went back: two entries, the mirror of the two that brought it in.
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
/// <b>Two entries because the receipt was two entries.</b> The gross leg leaves 311 and 681; the VAT leg
/// releases the liability the receipt carved onto 643. Whether a returned advance releases that VAT in
/// the same period was the accountant's question, not this code's, and the business answered it on
/// 2026-11-03: it does. The answer arrived the way a bookkeeping decision arrives here - as a mapping
/// row (<c>payment.refunded.vat</c> in <c>ledger_mapping_rules</c>) and not as an account name in a
/// class. Planning #854, #857.
/// </para>
/// </remarks>
public sealed class RefundJournalPoster
{
    /// <summary>The event every booked refund carries; it is the key an accountant queries.</summary>
    public const string RefundedEvent = "payment.refunded";

    /// <summary>The VAT half of the same refund, so the pair is queryable as one fact about one row.</summary>
    public const string RefundedVatEvent = "payment.refunded.vat";

    private readonly JournalPostingService _journal;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RefundJournalPoster> _logger;

    public RefundJournalPoster(
        JournalPostingService journal,
        ApplicationDbContext context,
        ILogger<RefundJournalPoster> logger)
    {
        _journal = journal;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Posts the refund: the gross pair and the VAT release. Idempotent on
    /// <c>(payment.refunded, Refund, refund.Id)</c> and
    /// <c>(payment.refunded.vat, Refund, refund.Id)</c>, so a retried sync or a replayed webhook books
    /// each leg once.
    /// </summary>
    /// <returns>The gross entry; the VAT leg is keyed on the same refund and queried with it.</returns>
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

        var entryDate = DateOnly.FromDateTime(refund.PaidAtUtc ?? SystemClock.UtcNow);
        var postedBy = refund.CreatedByUserId ?? refund.UserId ?? order.UserId;

        var entry = await _journal.PostAsync(
            new PostJournalEntryRequest
            {
                SourceEvent = RefundedEvent,
                SourceType = JournalSourceType.Refund,
                SourceId = refund.Id,
                // The day the acquirer returned the money, not the day a sync happened to look. A
                // refund confirmed days ago belongs in the period the money moved; if that period is
                // closed the posting says so instead of quietly dating the entry elsewhere.
                EntryDate = entryDate,
                AmountUah = refund.AmountUah,
                Description = $"Повернення за замовленням {order.Id}",
                // The operator who ordered the refund when there was one; the customer's own order
                // user otherwise, which is what payment.received already does for the receipt.
                PostedByUserId = postedBy,
            },
            cancellationToken);

        await BookVatReleaseAsync(refund, order, entryDate, postedBy, cancellationToken);

        _logger.LogInformation(
            "REFUND LEDGER: booked {Amount} UAH for refund {RefundId} (order {OrderId}) as entry {EntryNo}.",
            refund.AmountUah, refund.Id, order.Id, entry.EntryNo);

        return entry;
    }

    /// <summary>
    /// Releases the VAT the receipt carved out of the part of the advance that came back:
    /// Dr 643 / Cr 704, the exact mirror of <c>payment.received.vat</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The amount starts as the refunded gross with the same formula and the same rate the receipt
    /// used, never read off the receipt's entry. That is what keeps a refund from disagreeing with how
    /// its own sale was booked, and it is why the rate is written where the receipt writes it: today
    /// every sale is standard-rated, and if that stops being true both call sites change together.
    /// </para>
    /// <para>
    /// That division can round a kopeck past the carve when it is not the only correction on the
    /// order: two halves of a 1 724.00 ₴ payment each divide to 143.67, while the receipt carved
    /// 287.33 once. The last correction to run therefore takes only what remains of the carve - and a
    /// correction that runs after everything is released takes nothing, which is also the answer for a
    /// sale whose receipt booked no VAT at all (a promo order books nothing by decision).
    /// <see cref="CarvedVat"/> reads both figures from the books, so this cannot manufacture a credit
    /// against a liability the receipt never asserted.
    /// </para>
    /// </remarks>
    private async Task BookVatReleaseAsync(
        Payment refund,
        Order order,
        DateOnly entryDate,
        Guid postedByUserId,
        CancellationToken cancellationToken)
    {
        // A retried sync or a replayed webhook asks this again for a refund that is already booked.
        // The engine would return the existing entry anyway, but its amount is the booked one, and the
        // cap below would otherwise report a zero release for a leg that exists.
        var already = await CarvedVat.PostedAmountAsync(
            _context, RefundedVatEvent, JournalSourceType.Refund, refund.Id, cancellationToken);
        if (already > 0m)
        {
            _logger.LogInformation(
                "REFUND LEDGER: the VAT release for refund {RefundId} is already booked as {Vat} UAH; "
                + "not posting it again.", refund.Id, already);
            return;
        }

        var computed = VatCalculator.VatFromGross(refund.AmountUah, VatCalculator.StandardRate);
        if (computed <= 0m)
        {
            // A zero is not a posting - the engine refuses it - and an exempt advance carried no VAT
            // to release.
            return;
        }

        var carve = await CarvedVat.ForOrderAsync(_context, order.Id, cancellationToken);
        if (carve.CarvedUah <= 0m)
        {
            // No carve means no liability to take anything off. A promo sale is that by decision; an
            // order whose receipt was never booked is a hole for a person, not a credit to invent.
            _logger.LogWarning(
                "REFUND LEDGER: refund {RefundId} carries {Computed:0.00} UAH of VAT, but the receipt "
                + "of order {OrderId} booked no VAT of its own; the release was not booked.",
                refund.Id, computed, order.Id);
            return;
        }

        // The cap is what closes 643 at zero: the last correction takes the kopeck the divisions left.
        var vatUah = Math.Min(computed, carve.RemainingUah);
        if (vatUah <= 0m)
        {
            _logger.LogInformation(
                "REFUND LEDGER: refund {RefundId} carries {Computed:0.00} UAH of VAT, but the carve of "
                + "order {OrderId} ({Carved:0.00} UAH) is already released ({Taken:0.00} UAH); nothing "
                + "was booked.", refund.Id, computed, order.Id, carve.CarvedUah, carve.TakenUah);
            return;
        }

        await _journal.PostAsync(
            new PostJournalEntryRequest
            {
                SourceEvent = RefundedVatEvent,
                SourceType = JournalSourceType.Refund,
                // The same refund id as the gross leg: the key is the event plus the source, so the
                // two entries of one refund are two facts and not one colliding with the other.
                SourceId = refund.Id,
                EntryDate = entryDate,
                AmountUah = vatUah,
                Description = $"ПДВ з повернення за замовленням {order.Id}",
                PostedByUserId = postedByUserId,
            },
            cancellationToken);

        _logger.LogInformation(
            "REFUND LEDGER: released {Vat} UAH of VAT for refund {RefundId} ({Computed} UAH divided, "
            + "the rest of the order's carve was already released).",
            vatUah, refund.Id, computed);
    }
}
