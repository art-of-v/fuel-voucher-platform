using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Acquiring.UnappliedReceipts;

/// <summary>
/// Finds money the acquirer says it took that no payment row of ours holds.
/// </summary>
/// <remarks>
/// <para>
/// One definition of "unbooked", used by the close check that reports it and by the command that
/// books it. Two predicates would eventually disagree, and the disagreement would read as a
/// difference in the bank check that neither side can explain.
/// </para>
/// <para>
/// The candidates are the acquirer's own received statement rows in the period whose invoice has no
/// capture payment. A capture payment exists as soon as a checkout created an invoice, so this is
/// precisely the set of charges that reached the acquirer without the application ever knowing:
/// an invoice from before the payments table, a checkout that failed before its save, or money
/// taken against something that never became an order.
/// </para>
/// <para>
/// Only the LIVE merchant contributes. The sandbox merchant simulates payments with no money behind
/// them; booking those would put imaginary cash in the books and every later close would inherit it.
/// </para>
/// </remarks>
public static class UnappliedReceipts
{
    public static async Task<IReadOnlyList<UnbookedCapture>> FindAsync(
        ApplicationDbContext context,
        MonobankMerchant merchant,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken cancellationToken)
    {
        // The invoice predicate is in SQL and the status one is not: "success" is the acquirer's
        // vocabulary, matched as a substring (see MonobankStatementEntry.IsReceivedStatus), and a
        // LIKE built to imitate it would be a second definition of the same rule.
        var rows = await context.MonobankStatementEntries
            .AsNoTracking()
            .Where(e => e.Merchant == merchant
                     && e.InvoiceId != null
                     && e.InvoiceId != ""
                     && e.AmountKopecks > 0
                     && e.AcquirerCreatedAtUtc >= fromUtc
                     && e.AcquirerCreatedAtUtc < toUtcExclusive)
            .Select(e => new
            {
                InvoiceId = e.InvoiceId!,
                e.AmountKopecks,
                e.Status,
                e.AcquirerCreatedAtUtc,
                e.Rrn
            })
            .ToListAsync(cancellationToken);

        var received = rows
            .Where(r => MonobankStatementEntry.IsReceivedStatus(r.Status))
            .ToList();

        if (received.Count == 0)
        {
            return [];
        }

        var invoiceIds = received
            .Select(r => r.InvoiceId)
            .Distinct()
            .ToList();

        // A capture payment of ANY status counts as known. A Pending one is an invoice the checkout
        // issued and the customer has not settled by our records yet - its settlement and its receipt
        // belong to the webhook path, which knows the order; booking it here as unexplained money
        // would put the same charge in the books twice once that callback arrives.
        var known = await context.Payments
            .AsNoTracking()
            .Where(p => p.Provider == WebhookEventIdentity.Monobank
                     && p.Kind == PaymentKind.Capture
                     && p.ProviderInvoiceId != null
                     && invoiceIds.Contains(p.ProviderInvoiceId))
            .Select(p => p.ProviderInvoiceId!)
            .ToListAsync(cancellationToken);

        var knownInvoices = known.ToHashSet(StringComparer.Ordinal);

        return received
            .Where(r => !knownInvoices.Contains(r.InvoiceId))
            // Grouped by invoice because a payment is identified by its invoice: the unique index on
            // (provider, provider_invoice_id, kind) is what the booking writes against, so two rows
            // for one invoice are one amount to book, not two.
            .GroupBy(r => r.InvoiceId, StringComparer.Ordinal)
            .Select(g => new UnbookedCapture(
                g.Key,
                Money.FromKopecks(g.Sum(r => r.AmountKopecks ?? 0m)),
                g.Min(r => r.AcquirerCreatedAtUtc)!.Value,
                g.Count(),
                g.Select(r => r.Rrn).FirstOrDefault(rrn => !string.IsNullOrWhiteSpace(rrn))))
            .OrderBy(c => c.PaidAtUtc)
            .ToList();
    }
}

/// <param name="AmountUah">The gross the acquirer says it took.</param>
/// <param name="StatementRows">How many received statement rows carry this invoice. More than one
/// is worth a look: the amount is their sum, and a duplicate row would inflate it.</param>
/// <param name="Rrn">The acquirer's payment-system id, when the statement carries one - evidence a
/// dispute can be filed against.</param>
public sealed record UnbookedCapture(
    string InvoiceId,
    decimal AmountUah,
    DateTime PaidAtUtc,
    int StatementRows,
    string? Rrn);
