using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel.Time;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

public sealed partial class SettlePaidOrderCommandHandler
{
    /// <summary>
    /// Books the money. Two entries, because one cannot carry both facts: the gross lands in the
    /// bank, and the VAT inside it is a liability to the state. A promotional order books nothing:
    /// it is settled out of declared money, and its accounting entry is skipped by decision, not by
    /// accident.
    /// <para>
    /// <b>Why here and not in the Monobank webhook.</b> Every authority that can settle an order
    /// converges on this handler - the push webhook, the reconciliation poller, and once planning
    /// #867 lands the settlement channel - which is what makes one hook correct instead of three
    /// hooks that double-post. Whichever arrives second finds the entry already there and gets it
    /// back unchanged: <c>(source_event, source_type, source_id)</c> is a unique index, not a check,
    /// so two concurrent arrivals cannot both decide they are first.
    /// </para>
    /// <para>
    /// <b>Why the reconciliation path is the one the business chose.</b> A webhook fires when
    /// Monobank receives a request; reconciliation polls the invoice status until it settles. The
    /// confirmed choice on 2026-10-19 was to trust the settled state rather than the announcement.
    /// </para>
    /// <para>
    /// A failure here rolls the whole money step back, and that is deliberate. The order's status,
    /// the outbox event that hands it to fulfilment, the payment row that records the money and
    /// these entries are ONE transaction: the order is not committed before the receipt is booked.
    /// Money that is taken and not in the books is worse than a payment that is retried: the
    /// customer can pay twice, but a book that is silently short is discovered at month end.
    /// </para>
    /// </summary>
    private async Task PostReceiptToJournalAsync(Order order, CancellationToken cancellationToken)
    {
        // Planning ruling 2026-11-07 (promo cleanup): a promotional sale books nothing. The receipt and
        // its VAT leg are the signature of a real sale, and the promo channel must not write one, or the
        // books end up showing revenue that came out of a promo budget nobody ever recognised as such.
        // The money row, the draw and the fulfilment records the transfer itself; only the accounting
        // entry is skipped.
        //
        // The question asked is the ORDER's, not the route's: an order written on the acquirer route can
        // be paid out of the customer's declared balance, and that sale is a promotional sale whatever
        // route its checkout took. Production showed it on 2026-11-07: a 4 310,00 ₴ order paid out of
        // declared money booked a receipt and a VAT leg, and the books showed promo revenue.
        if (await OrderPayment.IsPromoOrderAsync(_context, order.Id, cancellationToken))
        {
            _logger.LogInformation(
                "SETTLEMENTS: order {OrderId} is a promo order, so no receipt was posted",
                order.Id);
            return;
        }

        // The VAT rate is a parameter, not a constant: fuel has carried a reduced rate in this
        // jurisdiction, and the business is the one that knows which it is registered for.
        const decimal vatRate = VatCalculator.StandardRate;
        var (netUah, vatUah) = VatCalculator.Split(order.Price, vatRate);

        await _journal.PostAsync(
            new PostJournalEntryRequest
            {
                SourceEvent = "payment.received",
                SourceType = JournalSourceType.Payment,
                SourceId = order.Id,
                EntryDate = DateOnly.FromDateTime(SystemClock.UtcNow),
                AmountUah = netUah + vatUah,
                Description = $"Оплата замовлення {order.Id}",
                PostedByUserId = order.UserId,
            },
            cancellationToken);

        // Zero-rated or exempt sales still owe an entry per event, or the idempotency key for
        // payment.received.vat would sit unused and a later non-exempt sale on the same order would
        // collide with it.
        await _journal.PostAsync(
            new PostJournalEntryRequest
            {
                SourceEvent = "payment.received.vat",
                SourceType = JournalSourceType.Payment,
                SourceId = order.Id,
                EntryDate = DateOnly.FromDateTime(SystemClock.UtcNow),
                AmountUah = vatUah,
                Description = $"ПДВ з оплати замовлення {order.Id}",
                PostedByUserId = order.UserId,
            },
            cancellationToken);

        _logger.LogInformation(
            "PAYMENTS: journal posted for order {OrderId}: net {Net} + VAT {Vat} = {Gross}",
            order.Id, netUah, vatUah, order.Price);
    }
}
