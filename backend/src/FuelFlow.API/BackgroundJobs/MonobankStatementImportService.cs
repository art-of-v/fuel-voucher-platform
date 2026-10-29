using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Acquiring.UnappliedReceipts;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Pulls the acquirer's statement into <c>monobank_statement_entries</c> (#118).
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="MonobankReconciliationService"/>, and the two must not be merged. That
/// job re-polls <em>orders</em> that are waiting for payment and forgets anything older than
/// <c>ReconciliationMaxAgeHours</c> - it is a recovery net for a lost webhook. This one reads
/// <em>the acquirer's</em> record and deliberately keeps re-reading a long tail, because a
/// transaction it settles late still has to reach the reconciliation. Opposite retention policies
/// over the same acquirer; folding them together would silently apply one policy to both.
/// </para>
/// <para>
/// One pass over a rolling window per merchant, upserted on (merchant, statement_id). A window
/// rather than a per-month fetch so a late correction is picked up on the next pass instead of being
/// lost for good.
/// <para>
/// The window is fetched as consecutive slices no wider than the acquirer accepts, because the
/// configured window is deliberately wider than that limit. Every earlier pass asked for the whole
/// window in one request, was refused with <c>to-'from' cannot be more than 31 days</c>, and imported
/// nothing at all - which left <c>monobank_statement_entries</c> empty and every reconciliation act
/// built on it empty too.
/// </para>
/// </remarks>
public class MonobankStatementImportService
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClientFactory _monobankClientFactory;
    private readonly MonobankOptions _options;
    private readonly ILogger<MonobankStatementImportService> _logger;

    public MonobankStatementImportService(
        ApplicationDbContext context,
        IMonobankClientFactory monobankClientFactory,
        IOptions<MonobankOptions> options,
        ILogger<MonobankStatementImportService> logger)
    {
        _context = context;
        _monobankClientFactory = monobankClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public virtual async Task ImportRecentStatementsAsync(CancellationToken cancellationToken = default)
    {
        // No-op when the integration is off. The mock's statement is synthetic and matches no
        // order, so importing it would fill the table with rows that every later reconciliation has
        // to classify as unmatched - noise stored in the one table that is supposed to be evidence.
        if (!_options.Enabled || !_options.StatementImportEnabled)
        {
            _logger.LogDebug(
                "Monobank statement import skipped (Enabled={Enabled}, StatementImportEnabled={Import})",
                _options.Enabled, _options.StatementImportEnabled);
            return;
        }

        var toUtc = SystemClock.UtcNow;
        var fromUtc = toUtc.AddDays(-_options.StatementImportWindowDays);
        var slices = MonobankStatementWindow.Split(fromUtc, toUtc);

        if (slices.Count == 0)
        {
            // A non-positive window asks for no period at all, and would otherwise look like a
            // perfectly healthy pass that found nothing. Said out loud, because the fix is one number
            // in configuration.
            _logger.LogWarning(
                "Statement import window of {Days} day(s) covers no period; check Monobank:StatementImportWindowDays",
                _options.StatementImportWindowDays);
            return;
        }

        foreach (var merchant in new[] { MonobankMerchant.Live, MonobankMerchant.Sandbox })
        {
            IMonobankClient client;

            try
            {
                // Resolved once per merchant rather than once per slice: a merchant with no token
                // raises here, and repeating that for every slice would say the same thing several
                // times over.
                client = _monobankClientFactory.ForMerchant(merchant);
            }
            catch (MonobankMerchantUnavailableException ex)
            {
                // Expected, not an error: a merchant with no token configured is simply not in use.
                _logger.LogInformation(ex, "Statement import skipped for the {Merchant} merchant", merchant);
                continue;
            }

            foreach (var slice in slices)
            {
                try
                {
                    await ImportMerchantAsync(client, merchant, slice.FromUtc, slice.ToUtc, cancellationToken);
                }
                catch (Exception ex)
                {
                    // One slice failing must not cost the rest of the period, and must not cost the
                    // other merchant either: the remaining slices are still importable now, and the
                    // next pass re-reads this one regardless.
                    _logger.LogError(
                        ex,
                        "Statement import failed for the {Merchant} merchant over [{From} - {To})",
                        merchant, slice.FromUtc, slice.ToUtc);
                }
            }
        }
    }

    private async Task ImportMerchantAsync(
        IMonobankClient client,
        MonobankMerchant merchant,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var items = await client.GetStatementAsync(fromUtc, toUtc, cancellationToken);

        // Defence in depth behind the client, which already refuses a row it cannot name. Two rows
        // sharing one key would throw out of ToDictionary and cost the whole period, and one nameless
        // row keyed as "" would overwrite the other forever - so the guard sits here rather than
        // trusting every future IMonobankClient to have made the same call.
        var nameable = items
            .Where(i => !string.IsNullOrWhiteSpace(i.StatementId))
            .ToList();

        if (nameable.Count != items.Count)
        {
            _logger.LogWarning(
                "Skipped {Count} {Merchant} statement row(s) that carry no id to file them under",
                items.Count - nameable.Count, merchant);
        }

        if (nameable.Count == 0)
        {
            _logger.LogDebug(
                "No statement rows for the {Merchant} merchant in [{From} - {To})", merchant, fromUtc, toUtc);
            return;
        }

        var now = SystemClock.UtcNow;

        var statementIds = nameable.Select(i => i.StatementId).Distinct().ToList();

        // One query for everything this pass touches, rather than a lookup per row: a busy month is
        // thousands of rows and the per-row variant turns that into thousands of round trips.
        var existing = await _context.MonobankStatementEntries
            .Where(e => e.Merchant == merchant && statementIds.Contains(e.StatementId))
            .ToDictionaryAsync(e => e.StatementId, cancellationToken);

        var inserted = 0;
        var updated = 0;

        foreach (var item in nameable)
        {
            var entry = new MonobankStatementEntry
            {
                Merchant = merchant,
                StatementId = item.StatementId,
                InvoiceId = item.InvoiceId,
                Reference = item.Reference,
                // The payment purpose the acquirer shows on the receipt. Evidence for a dispute,
                // never a match key - the reconciliation joins on the invoice id.
                Destination = item.Destination,
                AmountKopecks = item.Amount,
                Ccy = item.Ccy,
                Status = item.Status,
                // `date` is the financial operation time - the period the act filters on. It is not
                // the moment the row was created on the acquirer's side, and there is no separate
                // value for that: the statement reports one timestamp per row.
                AcquirerCreatedAtUtc = item.Date,
                MaskedCard = item.MaskedPan,
                Rrn = item.Rrn,
                ApprovalCode = item.ApprovalCode,
                RawPayload = item.RawJson,
                LastSeenAtUtc = now
            };

            if (existing.TryGetValue(item.StatementId, out var stored))
            {
                // Seen again. Only LastSeenAtUtc moves: the acquirer is the authority and its value
                // is what we keep, so re-importing a period cannot quietly rewrite history. A row
                // whose status genuinely did change upstream is corrected by re-importing the
                // period deliberately, not by letting a rolling window overwrite it.
                stored.LastSeenAtUtc = now;
                updated++;
                continue;
            }

            entry.Id = Guid.CreateVersion7();
            entry.FirstSeenAtUtc = now;
            _context.MonobankStatementEntries.Add(entry);
            existing[item.StatementId] = entry;
            inserted++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        await SettlePaymentsFromStatementAsync(merchant, nameable, now, cancellationToken);

        // Money the acquirer took that no payment row of ours holds (#780). Nothing is booked here:
        // an unexplained receipt becomes a liability under a named operator, and that is the
        // close-time command's job. Saying it in the log every pass is what keeps it from being
        // discovered at month end instead.
        if (merchant == MonobankMerchant.Live)
        {
            var unbooked = await UnappliedReceipts.FindAsync(_context, merchant, fromUtc, toUtc, cancellationToken);

            if (unbooked.Count > 0)
            {
                _logger.LogWarning(
                    "{Count} received {Merchant} capture(s) totalling {Total} UAH have no payment row and are not in the books: {Invoices}. " +
                        "Book them before the period closes (POST /api/admin/ledger/unapplied-receipts).",
                    unbooked.Count,
                    merchant,
                    unbooked.Sum(c => c.AmountUah),
                    string.Join(", ", unbooked.Select(c => c.InvoiceId)));
            }
        }

        _logger.LogInformation(
            "Imported the {Merchant} statement for [{From} - {To}): {Inserted} new, {Updated} already known",
            merchant, fromUtc, toUtc, inserted, updated);
    }

    /// <summary>
    /// Moves capture payments to what the acquirer's statement says about them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The statement is the authority on money in a way the webhook is not. Two things come out of
    /// this that nothing else in the system can supply:
    /// </para>
    /// <list type="number">
    /// <item><b>The time the money actually moved.</b> A callback carries the acquirer's
    /// <c>modifiedDate</c>, which moves every time the invoice is touched - including by a later,
    /// unrelated callback. The statement's <c>date</c> is the financial operation time. For a VAT
    /// payer that is a reporting-period boundary, so "roughly when the callback arrived" is not a
    /// substitute and this is where <c>payments.paid_at_utc</c> becomes the answer rather than an
    /// approximation.</item>
    ///
    /// <item><b>Payments nobody told us about.</b> An invoice paid while a callback was lost still
    /// appears here. Before this, such a payment was found only when an operator noticed an order
    /// stuck; now it is a row that says so.</item>
    /// </list>
    ///
    /// <para>
    /// The amount is NOT overwritten. The acquirer's figure is written beside ours into
    /// <c>provider_amount_kopecks</c> and the two are left to disagree, because they disagreeing is
    /// a finding this system should surface rather than resolve by picking a side. The same rule as
    /// the statement rows themselves: the acquirer is the authority on what it charged, and re-fetching
    /// a period must not quietly rewrite what we recorded.
    /// </para>
    ///
    /// <para>
    /// Only captures. Refund payments are matched by <c>ext_ref</c> in
    /// <c>RefundStatusSyncService.ApplyCancelListStatusAsync</c>, which owns that path and its tests;
    /// widening it here would duplicate a machine this job knows nothing about.
    /// </para>
    /// </remarks>
    private async Task SettlePaymentsFromStatementAsync(
        MonobankMerchant merchant,
        IReadOnlyList<MonobankStatementItem> items,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var invoiceIds = items
            .Select(i => i.InvoiceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (invoiceIds.Count == 0)
        {
            return;
        }

        var payments = await _context.Payments
            .Where(p => p.Kind == PaymentKind.Capture
                     && p.Provider == WebhookEventIdentity.Monobank
                     && p.ProviderInvoiceId != null
                     && invoiceIds.Contains(p.ProviderInvoiceId))
            .ToListAsync(cancellationToken);

        if (payments.Count == 0)
        {
            return;
        }

        var byInvoice = payments
            .GroupBy(p => p.ProviderInvoiceId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var settled = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.InvoiceId) || !byInvoice.TryGetValue(item.InvoiceId, out var candidates))
            {
                continue;
            }

            // Only what the acquirer actually reported as money received counts. A statement row in
            // any other state is evidence, but promoting a Pending capture to Succeeded off it would
            // assert a payment nobody made.
            if (!IsReceived(item.Status))
            {
                continue;
            }

            foreach (var payment in candidates)
            {
                payment.ProviderAmountKopecks = item.Amount;
                PaymentFactory.ApplyStatus(payment, PaymentStatus.Succeeded, item.Date ?? nowUtc, nowUtc);
                settled++;
            }
        }

        if (settled == 0)
        {
            return;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reconciled {Settled} capture payment(s) against the {Merchant} statement",
            settled, merchant);
    }

    /// <summary>
    /// The acquirer's own vocabulary, matched loosely on purpose - see
    /// <see cref="MonobankStatementEntry.IsReceivedStatus"/>, which is the single definition.
    /// </summary>
    private static bool IsReceived(string? acquirerStatus)
        => MonobankStatementEntry.IsReceivedStatus(acquirerStatus);
}