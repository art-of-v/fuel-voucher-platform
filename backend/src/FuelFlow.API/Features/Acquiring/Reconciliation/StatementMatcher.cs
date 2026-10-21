using System.Globalization;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;

namespace FuelFlow.Features.Acquiring.Reconciliation;

/// <summary>How one acquirer statement row lines up against what we recorded.</summary>
public enum StatementMatchStatus
{
    /// <summary>An order carries this invoice and the amounts agree.</summary>
    Matched,

    /// <summary>An order carries this invoice, but the acquirer says a different amount.
    /// This is the case the whole table exists to catch.</summary>
    AmountMismatch,

    /// <summary>No order and no refund of ours carries this invoice. Usually a payment against an
    /// order that was deleted as unpaid, or a settlement arriving after we gave up on it - real money
    /// with nothing on our side to point at. Reported, never dropped.</summary>
    Unmatched,

    /// <summary>The row is against a refund of ours, and no order of ours carries its invoice.</summary>
    /// <remarks>
    /// <para>
    /// <b>A row whose invoice we refunded is not one of these.</b> Monobank does not file a reversal
    /// as a row of its own: it nests the reversal under the payment it reverses, in a
    /// <c>cancelList</c>, and the entry carries no invoice id - so the row that carries our invoice
    /// is the <em>capture</em>, money in. It is matched against the order that carries the invoice,
    /// like any other capture, and its gross is part of the matched total. The refund we issued is
    /// reported beside the statement instead, in the act's own refunds section. See
    /// <see cref="StatementRefunds"/>.
    /// </para>
    /// <para>
    /// What is left is the row whose invoice only a refund of ours carries: the order is gone - it
    /// was deleted, or its invoice id moved on to a re-issued payment link - so the capture has no
    /// order to be matched against. The row is reported against the refund rather than dropped,
    /// because real money with nothing on our side to point at is reported, never dropped.
    /// </para>
    /// </remarks>
    RefundMatch,
}

/// <summary>One line of the reconciliation act.</summary>
/// <param name="Status">The verdict.</param>
/// <param name="Note">A human-readable reason, in the act's language, for anything not plain
/// <see cref="StatementMatchStatus.Matched"/>.</param>
public sealed record StatementMatchRow(
    string StatementId,
    StatementMatchStatus Status,
    string? InvoiceId,
    string? Reference,
    /// <summary>The payment purpose the acquirer shows on the receipt. Evidence, and never a key:
    /// the same string appears on thousands of unrelated payments.</summary>
    string? Destination,
    decimal? AcquirerAmountKopecks,
    decimal? AcquirerAmount,
    Guid? OrderId,
    decimal? LocalAmount,
    string? OrderReference,
    OrderStatus? OrderStatus,
    DateTime? AcquirerCreatedAtUtc,
    string? Note);

/// <summary>
/// Compares one period of acquirer statement against our own orders and refunds.
/// </summary>
/// <remarks>
/// Pure: it takes both sides already loaded and decides, touching neither the database nor the
/// clock. Every judgement it makes is a judgement about two sets of facts, and pulling it out from
/// the query means those judgements can be tested without a database at all.
/// </remarks>
public static class StatementMatcher
{
    public static IReadOnlyList<StatementMatchRow> Match(
        IReadOnlyList<MonobankStatementEntry> entries,
        IReadOnlyList<Order> orders,
        IReadOnlyList<Payment> refunds)
    {
        // Indexed by invoice id. Deliberately not by Reference: bulk checkouts all send
        // "FuelFlow Bundle" and every renewal sends "FuelFlow Voucher Renewal", so thousands of
        // distinct invoices share those strings and matching on them would attribute a single
        // acquirer row to whichever order happened to be first in the list.
        var ordersByInvoice = orders
            .Where(o => !string.IsNullOrEmpty(o.MonobankInvoiceId))
            .GroupBy(o => o.MonobankInvoiceId!)
            .ToDictionary(g => g.Key, g => g.First());

        // A refund is identified by the invoice it reverses - the same invoice the order carries.
        var refundsByInvoice = refunds
            .Where(r => r.Kind == PaymentKind.Refund && !string.IsNullOrEmpty(r.ProviderInvoiceId))
            .GroupBy(r => r.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        // For the human-readable note on a row that only a refund of ours carries.
        // refunds.ext_ref was a copy of the order's idempotency key that could drift from it;
        // reading the order directly is both the truth and one fewer place to fall out of sync.
        var referenceByOrderId = orders
            .ToDictionary(o => o.Id, o => o.IdempotencyKey ?? o.Id.ToString());

        var rows = new List<StatementMatchRow>(entries.Count);

        foreach (var entry in entries)
        {
            var amount = entry.AmountKopecks is null ? (decimal?)null : entry.AmountKopecks.Value / 100m;

            var invoiceId = entry.InvoiceId;

            // The capture. The ORDER is looked up first, and that order is what the row is: the
            // acquirer files the payment and nests a reversal under it, so a row whose invoice we
            // refunded is still the capture - money in - and it is matched against the order that
            // carries the invoice like any other capture. Its gross is part of the matched total.
            //
            // The refund we issued is reported beside the statement, in the act's own refunds
            // section, where it can be seen for what it is. Relabelling the capture as a reversal
            // would put the acquirer's gross in no total at all, and would read as money leaving
            // in a row that says money arrived.
            if (invoiceId is not null && ordersByInvoice.TryGetValue(invoiceId, out var order))
            {
                // The amount check. Kopecks become UAH by decimal division rather than the order's
                // price being multiplied by 100: division of an integer by 100m is exact in decimal,
                // where converting 978.57 to kopecks is a rounding decision, and a rounding decision
                // here is what makes a correct payment report as a mismatch.
                var agrees = amount is null || amount == order.Price;

                rows.Add(new StatementMatchRow(
                    entry.StatementId,
                    agrees ? StatementMatchStatus.Matched : StatementMatchStatus.AmountMismatch,
                    invoiceId, entry.Reference,
                    entry.Destination,
                    entry.AmountKopecks, amount,
                    order.Id, order.Price, order.Id.ToString(), order.Status,
                    entry.AcquirerCreatedAtUtc,
                    agrees
                        ? null
                        // Invariant culture, deliberately. A reconciliation act is a document an
                        // accountant reads and signs off; its figures must read the same on every
                        // server regardless of the machine's locale. Formatting 979.00 as "979,00"
                        // under a comma-decimal locale is the kind of thing nobody notices until
                        // the document is wrong in front of someone.
                        : FormattableString.Invariant(
                            $"Acquirer reports {amount:0.00}, the order was priced {order.Price:0.00}")));
                continue;
            }

            // No order of ours carries this invoice, but a refund of ours does: the order is gone -
            // deleted, or its invoice id moved on to a re-issued payment link - so the capture has
            // no order to be matched against and the row is reported against the refund instead.
            //
            // The refund's own order is nullable in the database (`ck_payments_refund_shape` asks
            // for a parent capture, not for an order), so this reads it as it is rather than
            // asserting an order that may not be there: a reconciliation row with a null order id
            // says "we cannot place this", which is the truth in that case.
            if (invoiceId is not null && refundsByInvoice.TryGetValue(invoiceId, out var refund))
            {
                var refundOrderId = refund.OrderId;
                var reference = refundOrderId is { } orderId
                    ? (referenceByOrderId.TryGetValue(orderId, out var known) ? known : orderId.ToString())
                    : invoiceId;

                rows.Add(new StatementMatchRow(
                    entry.StatementId, StatementMatchStatus.RefundMatch, invoiceId, entry.Reference,
                    entry.Destination,
                    entry.AmountKopecks, amount, refundOrderId, refund.AmountKopecks / 100m,
                    refund.ProviderInvoiceId, null, entry.AcquirerCreatedAtUtc,
                    $"Refund {refund.Id:N} gave this invoice back on {reference} - no order of ours carries it"));
                continue;
            }

            rows.Add(new StatementMatchRow(
                entry.StatementId, StatementMatchStatus.Unmatched, invoiceId, entry.Reference,
                entry.Destination,
                entry.AmountKopecks, amount,
                null, null, null, null, entry.AcquirerCreatedAtUtc,
                "No order or refund of ours carries this invoice"));
        }

        return rows;
    }

    /// <summary>Totals for the act's header. Unmatched rows are counted separately and never folded
    /// into the matched total - a summary that nets an unexplained payment into "reconciled" is how
    /// a reconciliation stops meaning anything.</summary>
    /// <param name="rows">The statement's rows, each with its verdict.</param>
    /// <param name="refundsIssued">The refunds we issued in the period. They carry their own
    /// figure and are never netted into the matched total: the matched total is about matching, not
    /// about net money, and a refund issued in the period can reverse a payment from an earlier one
    /// - so subtracting the two would mix the periods in a document someone signs. The capture the
    /// refund gave back is in the matched total, because that is money the acquirer took; the refund
    /// is the money that came back, and it has its own figure.</param>
    public static StatementMatchSummary Summarise(
        IReadOnlyList<StatementMatchRow> rows,
        IReadOnlyList<IssuedRefundRow>? refundsIssued = null)
    {
        var matched = rows.Where(r => r.Status == StatementMatchStatus.Matched).ToList();
        var mismatch = rows.Where(r => r.Status == StatementMatchStatus.AmountMismatch).ToList();
        var unmatched = rows.Where(r => r.Status == StatementMatchStatus.Unmatched).ToList();
        var refundMatches = rows.Where(r => r.Status == StatementMatchStatus.RefundMatch).ToList();

        return new StatementMatchSummary(
            rows.Count,
            matched.Count,
            mismatch.Count,
            unmatched.Count,
            refundMatches.Count,
            matched.Sum(r => r.AcquirerAmount ?? 0m),
            mismatch.Sum(r => (r.AcquirerAmount ?? 0m) - (r.LocalAmount ?? 0m)),
            unmatched.Sum(r => r.AcquirerAmount ?? 0m),
            refundsIssued?.Count ?? 0,
            refundsIssued?.Sum(r => r.Amount) ?? 0m);
    }
}

/// <param name="MatchedTotalUah">What the acquirer says it took, over the rows we can explain.
/// Gross: it is about matching, not about net money. A capture whose order we later refunded is
/// still a capture the acquirer took, so its gross is in here - the refund we gave back is
/// reported beside this figure, never subtracted from it.</param>
/// <param name="MismatchDeltaUah">Signed difference on the rows where the two disagree.</param>
/// <param name="UnmatchedTotalUah">Money the acquirer reports that we cannot account for at all.</param>
/// <param name="IssuedRefundCount">How many refunds we issued in the period.</param>
/// <param name="IssuedRefundTotalUah">The money we returned in the period. Its own figure, beside
/// the matched total and never folded into it - a refund issued in the period can reverse a payment
/// from an earlier one, and netting the two would mix the periods in a signed document.</param>
public sealed record StatementMatchSummary(
    int TotalRows,
    int MatchedCount,
    int MismatchCount,
    int UnmatchedCount,
    int RefundMatchCount,
    decimal MatchedTotalUah,
    decimal MismatchDeltaUah,
    decimal UnmatchedTotalUah,
    int IssuedRefundCount,
    decimal IssuedRefundTotalUah);