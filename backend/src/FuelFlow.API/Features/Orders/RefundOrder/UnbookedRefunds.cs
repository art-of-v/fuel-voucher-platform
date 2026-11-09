using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.RefundOrder;

/// <summary>
/// Acquirer-confirmed refunds the books do not hold, for the period close to name.
/// </summary>
/// <remarks>
/// <para>
/// The refund is booked when the confirmation arrives (<see cref="RefundJournalPoster"/>), and that
/// posting can be refused - a closed period, a mapping that is not there, a database that said no.
/// The confirmation path logs it and moves on, because a refund the bank has already made cannot wait
/// on the books; what was missing was a reader. A refund the platform confirmed and the ledger never
/// booked leaves the customer's money out of the accounts while every operational screen says the
/// refund completed, which is exactly the shape a close must not pass.
/// </para>
/// <para>
/// <b>How a refund counts as held.</b> By the key the posting uses - <c>payment.refunded</c> on the
/// refund's own id - or by a manual adjustment whose description names that id. The second half is
/// not a convenience: before the posting path existed, a confirmed refund was booked by hand exactly
/// that way, and the close check calling those sales unbooked would invite a re-drive that books them
/// twice. A human who wrote the refund's id into the entry's description did the same job the key
/// does, and this reads their word for it.
/// </para>
/// <para>
/// A draft does not count: it is not in the books, which is the thing being checked. Reversals count
/// as held, because a reversal is a second posted fact and a correction the operator made on purpose;
/// reading it as "never booked" would ask for the money to be booked a second time.
/// </para>
/// </remarks>
public static class UnbookedRefunds
{
    public static async Task<IReadOnlyList<UnbookedRefund>> FindAsync(
        ApplicationDbContext context,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken cancellationToken)
    {
        // PaidAtUtc is the acquirer's date for the money moving; a row confirmed by a path that did
        // not stamp it still belongs to the period it was last touched in, and dropping it would hide
        // exactly the refund this check exists for.
        var confirmed = await context.Payments
            .AsNoTracking()
            .Where(p => p.Kind == PaymentKind.Refund
                     && p.Status == PaymentStatus.Succeeded
                     && (p.PaidAtUtc ?? p.UpdatedAtUtc) >= fromUtc
                     && (p.PaidAtUtc ?? p.UpdatedAtUtc) < toUtcExclusive)
            .Select(p => new
            {
                p.Id,
                p.OrderId,
                p.AmountUah,
                PaidAtUtc = p.PaidAtUtc ?? p.UpdatedAtUtc,
            })
            .OrderBy(p => p.PaidAtUtc)
            .ToListAsync(cancellationToken);

        if (confirmed.Count == 0)
            return [];

        var refundIds = confirmed.Select(c => c.Id).ToList();

        var keyed = await context.JournalEntries
            .AsNoTracking()
            .Where(e => e.SourceEvent == RefundJournalPoster.RefundedEvent
                     && e.SourceType == JournalSourceType.Refund
                     && e.SourceId != null
                     && refundIds.Contains(e.SourceId.Value)
                     && e.Status != JournalEntryStatus.Draft)
            .Select(e => e.SourceId!.Value)
            .ToListAsync(cancellationToken);

        var held = keyed.ToHashSet();
        var unbooked = new List<UnbookedRefund>(confirmed.Count);

        foreach (var refund in confirmed)
        {
            if (held.Contains(refund.Id))
                continue;

            // One query per refund that the key does not explain, which is normally none and never
            // more than a handful: the period has only the refunds the platform confirmed in it.
            var idText = refund.Id.ToString();
            var namedByHand = await context.JournalEntries
                .AsNoTracking()
                .AnyAsync(e => e.SourceType == JournalSourceType.Adjustment
                            && e.Status != JournalEntryStatus.Draft
                            && e.Description.Contains(idText), cancellationToken);

            if (!namedByHand)
            {
                unbooked.Add(new UnbookedRefund(
                    refund.Id, refund.OrderId, refund.AmountUah, refund.PaidAtUtc));
            }
        }

        return unbooked;
    }
}

/// <param name="PaymentId">The refund row the acquirer confirmed.</param>
/// <param name="OrderId">The order the money went back against; null only if the row lost it.</param>
/// <param name="AmountUah">The gross the acquirer returned.</param>
/// <param name="PaidAtUtc">When the acquirer reported the money moving.</param>
public sealed record UnbookedRefund(
    Guid PaymentId,
    Guid? OrderId,
    decimal AmountUah,
    DateTime PaidAtUtc);
