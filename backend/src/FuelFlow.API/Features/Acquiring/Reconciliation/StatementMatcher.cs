using System.Globalization;
using FuelFlow.Features.Orders.SharedModels;

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

    /// <summary>The acquirer tagged this row against a refund we issued.</summary>
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
    long? AcquirerAmountKopecks,
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
        IReadOnlyList<Refund> refunds)
    {
        // Indexed by invoice id. Deliberately not by Reference: bulk checkouts all send
        // "FuelFlow Bundle" and every renewal sends "FuelFlow Voucher Renewal", so thousands of
        // distinct invoices share those strings and matching on them would attribute a single
        // acquirer row to whichever order happened to be first in the list.
        var ordersByInvoice = orders
            .Where(o => !string.IsNullOrEmpty(o.MonobankInvoiceId))
            .GroupBy(o => o.MonobankInvoiceId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Same reasoning: a refund is identified by the invoice it reverses.
        var refundsByInvoice = refunds
            .Where(r => !string.IsNullOrEmpty(r.InvoiceId))
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var rows = new List<StatementMatchRow>(entries.Count);

        foreach (var entry in entries)
        {
            var amount = entry.AmountKopecks is null ? (decimal?)null : entry.AmountKopecks.Value / 100m;

            var invoiceId = entry.InvoiceId;

            if (invoiceId is not null && refundsByInvoice.TryGetValue(invoiceId, out var refund))
            {
                rows.Add(new StatementMatchRow(
                    entry.StatementId, StatementMatchStatus.RefundMatch, invoiceId, entry.Reference,
                    entry.AmountKopecks, amount, refund.OrderId, refund.Amount / 100m,
                    refund.InvoiceId, null, entry.AcquirerCreatedAtUtc,
                    $"Reversal of refund {refund.Id:N} on {refund.ExtRef}"));
                continue;
            }

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

            rows.Add(new StatementMatchRow(
                entry.StatementId, StatementMatchStatus.Unmatched, invoiceId, entry.Reference,
                entry.AmountKopecks, amount,
                null, null, null, null, entry.AcquirerCreatedAtUtc,
                "No order or refund of ours carries this invoice"));
        }

        return rows;
    }

    /// <summary>Totals for the act's header. Unmatched rows are counted separately and never folded
    /// into the matched total - a summary that nets an unexplained payment into "reconciled" is how
    /// a reconciliation stops meaning anything.</summary>
    public static StatementMatchSummary Summarise(IReadOnlyList<StatementMatchRow> rows)
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
            unmatched.Sum(r => r.AcquirerAmount ?? 0m));
    }
}

/// <param name="MatchedTotalUah">What the acquirer says it took, over the rows we can explain.</param>
/// <param name="MismatchDeltaUah">Signed difference on the rows where the two disagree.</param>
/// <param name="UnmatchedTotalUah">Money the acquirer reports that we cannot account for at all.</param>
public sealed record StatementMatchSummary(
    int TotalRows,
    int MatchedCount,
    int MismatchCount,
    int UnmatchedCount,
    int RefundMatchCount,
    decimal MatchedTotalUah,
    decimal MismatchDeltaUah,
    decimal UnmatchedTotalUah);