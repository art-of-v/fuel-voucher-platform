using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Acquiring;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
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

        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-_options.StatementImportWindowDays);

        foreach (var merchant in new[] { MonobankMerchant.Live, MonobankMerchant.Sandbox })
        {
            // One merchant failing must not abort the other. Sandbox being down says nothing about
            // live, and the act is per-merchant anyway.
            try
            {
                await ImportMerchantAsync(merchant, fromUtc, toUtc, cancellationToken);
            }
            catch (MonobankMerchantUnavailableException ex)
            {
                // Expected, not an error: a merchant with no token configured is simply not in use.
                _logger.LogInformation(ex, "Statement import skipped for the {Merchant} merchant", merchant);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Statement import failed for the {Merchant} merchant", merchant);
            }
        }
    }

    private async Task ImportMerchantAsync(
        MonobankMerchant merchant,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var client = _monobankClientFactory.ForMerchant(merchant);
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

        var now = DateTime.UtcNow;

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

        _logger.LogInformation(
            "Imported the {Merchant} statement for [{From} - {To}): {Inserted} new, {Updated} already known",
            merchant, fromUtc, toUtc, inserted, updated);
    }
}