using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel.Time;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed partial class ProcessMonobankWebhookCommandHandler
{
    /// <summary>
    /// Books the money. Two entries, because one cannot carry both facts: the gross lands in the
    /// bank, and the VAT inside it is a liability to the state.
    /// <para>
    /// <b>Why here and not in the Monobank webhook.</b> Both arrival paths - the push webhook and
    /// the reconciliation poller - converge on this handler, which is what makes one hook correct
    /// instead of two hooks that double-post. Whichever arrives second finds the entry already there
    /// and gets it back unchanged: <c>(source_event, source_type, source_id)</c> is a unique index,
    /// not a check, so two concurrent arrivals cannot both decide they are first.
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