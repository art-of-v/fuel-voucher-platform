using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;

namespace FuelFlow.Features.Acquiring.Reconciliation;

/// <summary>
/// The refunds we issued inside the period, as the act's own section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this section exists at all.</b> Monobank does not put a reversal on the statement as a row
/// of its own: it nests the reversal under the payment it reverses, in a <c>cancelList</c>, and the
/// entry carries no invoice id - only amount, currency, date, masked pan, approval code and rrn.
/// Filing those entries as statement rows would put money out in a document an accountant signs with
/// nothing to point at, and matching them to a payment by pan and amount would be a guess. The
/// decision taken by the product owner on 2026-10-21 (planning #297) is therefore the third option:
/// the acquirer's statement stays the authority on <em>what arrived</em>, and the act reconciles the
/// refunds <em>from our side</em> - the refunds we issued in the period - beside it.
/// </para>
/// <para>
/// <b>When a refund counts as issued.</b> When the money actually left: the acquirer confirmed it and
/// the payment says <c>Succeeded</c>. A refund still in flight is a request, not money, and it is not
/// in this figure - the acquirer's statement is what says whether it arrived. A failed refund never
/// left either.
/// </para>
/// <para>
/// <b>Why there is no net figure.</b> A refund issued in the period can reverse a payment from an
/// earlier one, so netting the two would mix the periods in a document someone signs. The matched
/// total stays gross and the refunds get their own figure; a refund that reverses an earlier period's
/// payment says so on its own row.
/// </para>
/// </remarks>
public static class StatementRefunds
{
    /// <summary>
    /// When the money left, as we know it.
    /// </summary>
    /// <remarks>
    /// The acquirer's own date for the reversal - the same kind of date a statement row carries, and
    /// the one that makes "the refunds of October" mean the same thing on both sides of the act. The
    /// row's own creation date is the fallback for a refund the acquirer confirmed without a date;
    /// the database requires a paid-at on every succeeded payment, so this is a safety net rather
    /// than a path anything should take.
    /// </remarks>
    public static DateTime IssuedAt(Payment refund) => refund.PaidAtUtc ?? refund.CreatedAtUtc;

    /// <summary>
    /// Whether this refund is one of the ones we issued inside the period.
    /// </summary>
    /// <remarks>
    /// The single definition of "issued", used both by the query that fetches the refunds and by the
    /// composition that turns them into rows. The query deliberately asks for a superset of it - it
    /// does not check the status - so a change to which statuses count can never lose a row to a
    /// narrower query than the rule that decides what to report.
    /// </remarks>
    public static bool IsIssuedInPeriod(Payment refund, DateTime fromUtc, DateTime toUtc)
    {
        var issuedAt = IssuedAt(refund);

        return refund.Status == PaymentStatus.Succeeded
            && issuedAt >= fromUtc
            && issuedAt < toUtc;
    }

    /// <summary>
    /// The act's refunds section: the refunds issued inside the period, each with the payment it
    /// reverses.
    /// </summary>
    /// <param name="refunds">Every refund the act loaded - the ones the statement mentions and the
    /// ones issued in the period. The ones that are neither are filtered out here.</param>
    /// <param name="orders">The orders behind those refunds, for the fallback date.</param>
    /// <param name="captures">The captures those refunds reverse - the payment the money went back
    /// against.</param>
    public static IReadOnlyList<IssuedRefundRow> Compose(
        IReadOnlyList<Payment> refunds,
        IReadOnlyList<Order> orders,
        IReadOnlyList<Payment> captures,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var capturesById = captures.ToDictionary(c => c.Id);
        var ordersById = orders.ToDictionary(o => o.Id);

        var rows = new List<IssuedRefundRow>();

        foreach (var refund in refunds
                     .Where(r => IsIssuedInPeriod(r, fromUtc, toUtc))
                     .OrderBy(r => IssuedAt(r))
                     .ThenBy(r => r.Id))
        {
            // The capture is the payment the refund gives back against: a refund always points at
            // one (`ck_payments_refund_shape` refuses a refund without a parent), and its paid-at is
            // the acquirer's own date for the money that came in.
            var capture = refund.ParentPaymentId is { } parent && capturesById.TryGetValue(parent, out var found)
                ? found
                : null;

            // When we took the money. The capture's paid-at is the truth; the order's created-at is
            // the fallback for a capture that never settled, because the order is still what we
            // booked the sale on.
            var reversedAt = capture?.PaidAtUtc
                ?? (refund.OrderId is { } orderId && ordersById.TryGetValue(orderId, out var order)
                    ? order.CreatedAtUtc
                    : null);

            // The payment this refund gives back against, when it belongs to an earlier period than
            // the one this act covers.
            var earlierPaymentAt = reversedAt is { } at && at < fromUtc ? at : (DateTime?)null;

            // Invariant culture, like every other figure in the act: this text is read in a signed
            // document, and it must read the same on every server.
            var note = earlierPaymentAt is { } earlier
                ? FormattableString.Invariant($"Reverses a payment from {earlier:yyyy-MM-dd} - an earlier period than this act")
                : null;

            rows.Add(new IssuedRefundRow(
                refund.Id,
                refund.OrderId,
                refund.ProviderInvoiceId,
                refund.AmountKopecks / 100m,
                IssuedAt(refund),
                reversedAt,
                earlierPaymentAt.HasValue,
                note));
        }

        return rows;
    }
}
