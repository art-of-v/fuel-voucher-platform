using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.RefundOrder;

/// <summary>
/// Says out loud when a refund's amount changes after the books already carry it.
///
/// <c>payment.refunded</c> is keyed on the refund payment, so a refund that Monobank confirmed and an
/// operator then re-priced keeps the entry that was posted on the day the money moved. The engine will
/// not write it a second time - that is what makes the posting safe to retry - which means the row and
/// the entry disagree and nothing says so. An accountant closing the period would find the difference
/// by hand, in a ledger that looks complete.
///
/// How a returned advance that was then re-priced is corrected is the accountant's decision, not this
/// code's: reversing and re-booking under the same key is refused by the idempotency index, so the fix
/// is a second entry by hand. Recording the gap is all this does (planning #854).
/// </summary>
public static class RefundRepriceDrift
{
    /// <summary>
    /// Writes an audit row when the journal holds a posted entry for this refund whose amount no
    /// longer matches it. Silent when nothing was booked yet - the confirmation will book the new
    /// figure and there is no drift.
    /// </summary>
    public static async Task RecordIfBookedAmountDriftedAsync(
        ApplicationDbContext context,
        AuditEventService auditEvents,
        Payment refund,
        Order order,
        Guid? changedByUserId,
        string? changedByUserName,
        CancellationToken cancellationToken)
    {
        var bookedUah = await context.JournalEntries
            .AsNoTracking()
            .Where(e => e.SourceEvent == RefundJournalPoster.RefundedEvent
                     && e.SourceType == JournalSourceType.Refund
                     && e.SourceId == refund.Id
                     && e.Status == JournalEntryStatus.Posted)
            .SelectMany(e => e.Lines)
            .SumAsync(l => l.DebitUah, cancellationToken);

        if (bookedUah <= 0m || bookedUah == refund.AmountUah)
        {
            return;
        }

        // The VAT leg moves with the gross one, and a person posting the difference by hand needs
        // both figures: the booked VAT is read rather than derived, because a refund booked before
        // the VAT rule existed has no leg at all and the gap is the whole of the new one.
        var bookedVatUah = await context.JournalEntries
            .AsNoTracking()
            .Where(e => e.SourceEvent == RefundJournalPoster.RefundedVatEvent
                     && e.SourceType == JournalSourceType.Refund
                     && e.SourceId == refund.Id
                     && e.Status == JournalEntryStatus.Posted)
            .SelectMany(e => e.Lines)
            .SumAsync(l => l.DebitUah, cancellationToken);

        var vatDifferenceUah =
            VatCalculator.VatFromGross(refund.AmountUah, VatCalculator.StandardRate) - bookedVatUah;

        var summary =
            $"Refund re-priced to {refund.AmountUah:0.00} UAH after {bookedUah:0.00} UAH was booked; the difference of "
            + $"{refund.AmountUah - bookedUah:0.00} UAH is not in the journal and needs an entry by hand";
        if (vatDifferenceUah != 0m)
        {
            summary += $", as does {vatDifferenceUah:0.00} UAH of its VAT on {RefundJournalPoster.RefundedVatEvent}";
        }

        auditEvents.RecordEvent(
            aggregateType: "Refund",
            aggregateId: refund.Id.ToString(),
            eventType: "RefundAmountCorrectedAfterBooking",
            oldValue: JsonSerializer.Serialize(new { bookedUah, bookedVatUah }),
            newValue: JsonSerializer.Serialize(new { amountUah = refund.AmountUah }),
            changedByUserId: changedByUserId ?? Guid.Empty,
            changedByUserName: changedByUserName,
            summary: summary,
            providerId: order.LineItems.FirstOrDefault()?.Brand ?? "unknown");

        await context.SaveChangesAsync(cancellationToken);
    }
}
