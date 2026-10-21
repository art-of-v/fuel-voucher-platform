using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Nightly garbage-collector for high-churn operational tables. Prunes rows that have outlived their
/// usefulness — spent/expired OTPs, dead refresh tokens, processed outbox events, read notifications,
/// aged error logs and stale push tokens — each past its own fixed retention window.
///
/// Fail-safe by default: while <c>DataRetention:Enabled</c> is off (the default) the job runs
/// read-only, logging how many rows *would* be purged per table so an admin can gauge the impact
/// before opting in. Only once enabled does it delete.
///
/// Every predicate excludes live data by construction — a valid (unexpired, unrevoked) token, an
/// unprocessed outbox event and an unread notification are never matched — so the retention window
/// is only ever an audit buffer on top of "already dead". Orders are deliberately out of scope;
/// their irreversible purge is a separate switch (see <see cref="DeletedUnpaidOrderCleanupService"/>).
///
/// Deletes are batched (load + <c>RemoveRange</c>, mirroring <see cref="DeletedUnpaidOrderCleanupService"/>): each
/// batch is one small transaction, so a large first-run backlog drains over successive nightly runs
/// rather than one unbounded delete holding locks and bloating WAL.
/// </summary>
public class DataRetentionService
{
    private const int BatchSize = 1000;

    // Fixed windows (whole days). Public so tests and any future admin surface reference the exact
    // policy rather than re-hard-coding literals.
    public const int VerificationCodeRetentionDays = 7;
    public const int RefreshTokenRetentionDays = 30;
    public const int OutboxEventRetentionDays = 30;
    public const int NotificationRetentionDays = 90;
    public const int ErrorLogRetentionDays = 90;
    public const int PushTokenRetentionDays = 90;

    /// <summary>
    /// A year, unlike everything else here: <c>provider_event_outbox</c> is the audit trail of admin
    /// changes - who changed which price or package, with the before and after values - so it is kept
    /// long enough to still answer who touched this last season, and pruned only as a floor on growth.
    /// </summary>
    public const int ProviderEventRetentionDays = 365;

    /// <summary>
    /// Matches <see cref="PushTokenRetentionDays"/>: a device the app has not reported from for a
    /// whole season is an uninstalled app, and a reinstall registers a fresh device.
    /// </summary>
    public const int DeviceRetentionDays = 90;

    /// <summary>
    /// Import batches and their error rows are a supplier-delivery record, kept a full season so a
    /// cost dispute can still be traced to the PDF it came from. Never shorter: a supplier invoice
    /// arrives on its own schedule, not ours.
    /// </summary>
    public const int VoucherImportRetentionDays = 365;

    /// <summary>
    /// A QR encoding configuration older than a season will never be derived again — a re-import
    /// re-reads the parameters off the paper rather than reusing this row.
    /// </summary>
    public const int QrParametersRetentionDays = 365;

    /// <summary>
    /// A season, matching the import and QR windows. <c>support_messages</c> is a contact form:
    /// email, user agent, IP and the message itself, with no FK to a user and no reader anywhere in
    /// the product - nothing queries the table except this job. It is pure PII with no audit value,
    /// so a year is more than long enough to answer "did this person contact us, and when" and short
    /// enough that the data-minimisation obligation has a floor.
    /// <para>
    /// This was deliberately uncovered until the product owner set a window on it; the previous
    /// comment said "no stated window", which is a question, not a decision.
    /// </para>
    /// </summary>
    public const int SupportMessageRetentionDays = 365;

    /// <summary>
    /// Customer feedback from the Telegram bot. Shorter than <see cref="SupportMessageRetentionDays"/>
    /// because these rows are linked to a real account (<c>user_id</c>) and carry the customer's own
    /// words, so they are both product backlog and personal data. 180 days is long enough to act on
    /// a quarterly roadmap cycle; past that the backlog has already been triaged and the row is
    /// only a liability.
    /// <para>
    /// Replies are purged with the same window. The FK is Restrict precisely so this ordering is
    /// explicit: replies first, then the messages they answer.
    /// </para>
    /// </summary>
    public const int FeedbackMessageRetentionDays = 180;

    // Deliberately NOT covered, and the reason matters more than the list:
    //
    //   movement_batches           - the settlement the exchange pairs belong to (#319). "Who do we
    //                                 settle with, for what, at what cost" is answerable only from
    //                                 these rows and the ledger's pairs, and VOUCHER_LIFECYCLE.md calls
    //                                 the system itself the record (suppliers issue no invoices, there
    //                                 is no paper to check against). Pruning them destroys the audit
    //                                 trail. It is also the only place a batch's lifecycle (opened,
    //                                 confirmed, rejected) is written down at all.
    //   order_line_items (renewals)- what the customer paid and how the term moved. Their order_id was
    //                                 CASCADE, the only thing that may have removed one, and flipped to
    //                                 RESTRICT when voucher_renewal_items was dissolved (20261017065731).
    //   operator_voucher_renewals  - the same settlement fact for the operator path.
    //   monobank_statement_entries - the acquirer's immutable evidence. Re-import must never
    //                                 rewrite a stored amount (acquiring-statement-118.md), so a
    //                                 prune here would be a silent edit of the ledger.
    //   fuel_vouchers (soft-deleted)- paid-for paper. There is no window during which this is ok.
    //   voucher_events             - "who did this, and when", for everything that happened to a
    //                                 voucher without moving it. The redemption is the one that
    //                                 matters: fuel is dispensed at a pump and nobody signs anything,
    //                                 so if this row is gone the only proof a customer ever used fuel
    //                                 is a status column with no author and no timestamp. The pairing
    //                                 CHECK already refuses to store an author-less action, which
    //                                 makes that promise worth more than the row is long.
    //   voucher_movements          - "where is this paper". A voucher's custody history starts at the
    //                                 warehouse: import puts it in the pool, the sale puts it in a
    //                                 customer's hands, a recall brings it back. It cannot be rebuilt
    //                                 from fuel_vouchers, because one row there is updated in place for
    //                                 the voucher's whole life and never says where it was. It is also
    //                                 where an exchange's pairs live now (#319), so the movement IS the
    //                                 record, there is no other copy.


    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly ILogger<DataRetentionService> _logger;

    /// <summary>
    /// Tables whose purge failed this run, so the summary can say so instead of reporting a clean
    /// number. Safe as an instance field because the service is scoped (ServiceSetup.cs:447) and
    /// Hangfire resolves one scope per execution - but it is cleared at the start of every run
    /// anyway, so a second call on the same instance cannot inherit the previous run's failures.
    /// </summary>
    private readonly List<(string Table, string Reason)> _failures = [];

    public DataRetentionService(
        ApplicationDbContext context,
        RuntimeSettingsService settings,
        ILogger<DataRetentionService> logger)
    {
        _context = context;
        _settings = settings;
        _logger = logger;
    }

    public virtual async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        _failures.Clear();

        var enabled = await _settings.IsDataRetentionEnabledAsync(cancellationToken);
        var dryRun = !enabled;

        // One clock read shared by every predicate so a slow run can't apply drifting cutoffs.
        var now = DateTime.UtcNow;
        var verificationCutoff = now.AddDays(-VerificationCodeRetentionDays);
        var refreshTokenCutoff = now.AddDays(-RefreshTokenRetentionDays);
        var outboxCutoff = now.AddDays(-OutboxEventRetentionDays);
        var notificationCutoff = now.AddDays(-NotificationRetentionDays);
        var errorLogCutoff = now.AddDays(-ErrorLogRetentionDays);
        var pushTokenCutoff = now.AddDays(-PushTokenRetentionDays);
        var providerEventCutoff = now.AddDays(-ProviderEventRetentionDays);
        var deviceCutoff = now.AddDays(-DeviceRetentionDays);
        var voucherImportCutoff = now.AddDays(-VoucherImportRetentionDays);
        var qrParametersCutoff = now.AddDays(-QrParametersRetentionDays);
        var supportMessageCutoff = now.AddDays(-SupportMessageRetentionDays);
        var feedbackMessageCutoff = now.AddDays(-FeedbackMessageRetentionDays);

        // Devices are purged by TWO predicates rather than the one
        // `Status == Revoked || LastSeenAt < cutoff` they read as, and the reason is the planner,
        // not readability. A `BitmapOr` cannot usefully combine a bare status comparison with an
        // indexed range, so the combined delete stays on a Seq Scan even with an index on
        // LastSeenAt - measured 28.5 ms against 3.7 ms for the split form, at 250k rows.
        //
        // The two sets are disjoint (the second excludes Revoked) and together they match exactly
        // what the OR matched, so the summed count is unchanged - verified by test against the OR.
        var revokedDeviceCount = await PurgeAsync(
            "devices", _context.Devices.Where(d => d.Status == DeviceStatus.Revoked),
            dryRun, cancellationToken);

        var staleDeviceCount = await PurgeAsync(
            "devices", _context.Devices.Where(d => d.Status != DeviceStatus.Revoked && d.LastSeenAt < deviceCutoff),
            dryRun, cancellationToken);

        // __RESULTS__
        var results = new (string Table, int Count)[]
        {
            // Spent (used) or expired OTPs, aged past the window. A still-valid code — unused and
            // unexpired — is never matched, whatever its age.
            ("verification_codes", await PurgeAsync("verification_codes", 
                _context.VerificationCodes.Where(c =>
                    (c.IsUsed || c.ExpiresAtUtc < now) && c.CreatedAtUtc < verificationCutoff),
                dryRun, cancellationToken)),

            // Revoked or expired refresh tokens, aged past the window. An active session's token
            // (unrevoked and unexpired) is excluded regardless of age.
            ("refresh_tokens", await PurgeAsync("refresh_tokens", 
                _context.RefreshTokens.Where(t =>
                    (t.IsRevoked || t.ExpiresAtUtc < now) && t.CreatedAtUtc < refreshTokenCutoff),
                dryRun, cancellationToken)),

            // Already-processed outbox events, aged by when they were processed, PLUS the events
            // that have no consumer at all, aged by when they happened.
            //
            // "Unprocessed work is never touched" is the right rule for work, and it stays: the
            // three event types that something reads are still work while they are unprocessed,
            // and they are kept. The five types that nothing reads are not work - they are the
            // record of something that happened, and no consumer will ever pick them up, so
            // keeping them as "unprocessed" meant the retention never touched them and the table
            // grew without bound: every order, every expiry and every payment left a row behind
            // for good, and the safety net that reads this table had to walk all of them.
            ("outbox_events", await PurgeAsync("outbox_events",
                _context.OutboxEvents.Where(o =>
                    (o.Processed && o.ProcessedAtUtc != null && o.ProcessedAtUtc < outboxCutoff)
                    || (o.CreatedAtUtc < outboxCutoff && !ConsumedOutboxEventTypes.All.Contains(o.EventType))),
                dryRun, cancellationToken)),

            // Read notifications only; unread ones are kept indefinitely so a user never loses an
            // unseen message to retention.
            ("notifications", await PurgeAsync("notifications", 
                _context.Notifications.Where(n =>
                    n.IsRead && n.CreatedAtUtc < notificationCutoff),
                dryRun, cancellationToken)),

            // Error logs are pure diagnostics; prune purely by age.
            ("error_logs", await PurgeAsync("error_logs", 
                _context.ErrorLogs.Where(e => e.LoggedAtUtc < errorLogCutoff),
                dryRun, cancellationToken)),

            // Push tokens not seen in the window: a live device refreshes LastSeenAtUtc regularly,
            // so a long-silent token is unreachable and safe to drop (a reinstall mints a new one).
            ("push_tokens", await PurgeAsync("push_tokens", 
                _context.PushTokens.Where(p => p.LastSeenAtUtc < pushTokenCutoff),
                dryRun, cancellationToken)),

            // The admin change audit trail, aged purely by when the change happened. There is no
            // processed state to exclude here: a row is a record of something a person did, so the
            // only question is how far back it is still worth answering for.
            ("provider_event_outbox", await PurgeAsync("provider_event_outbox", 
                _context.AuditEventOutbox.Where(e => e.ChangedAtUtc < providerEventCutoff),
                dryRun, cancellationToken)),

            // A revoked device is dead the moment it is revoked, whatever its age; a device the app
            // has not reported from for the window is an uninstalled app. Both are unreachable, and a
            // reinstall registers a fresh device rather than reviving this row. Counted above, in
            // two queries, so the LastSeenAt index is usable.
            ("devices", revokedDeviceCount + staleDeviceCount),

            // A finished import and its error rows, aged by completion. An in-flight import
            // (CompletedAtUtc null) is never matched however old it started. Ordered last so the
            // import errors cascade to their batch in the same run.
            //
            // The reference check is what makes this safe, and it is the same shape as the
            // qr_parameters purge below: purchase_batches.import_job_id is Restrict and purchase
            // batches are never purged (they are costing records - the import's price history).
            // Without the check, the first import that had a cost entered against it made
            // DELETE FROM voucher_imports raise 23503, and because PurgeAsync saves per table and
            // throws on the first failure, every table listed after this one - qr_parameters,
            // support_messages, feedback_message_replies, feedback_messages - was silently skipped
            // on that run and on every run after. DeletedUnpaidOrderCleanupService guards refunds
            // and renewal items for exactly this reason; purchase_batches was missed here.
            ("voucher_imports", await PurgeAsync(
                "voucher_imports",
                _context.VoucherImports.Where(i =>
                    i.CompletedAtUtc != null && i.CompletedAtUtc < voucherImportCutoff &&
                    !_context.PurchaseBatches.IgnoreQueryFilters()
                        .Any(b => b.ImportJobId == i.Id)),
                dryRun, cancellationToken)),

            // A QR config nothing points at any more. The reference check is what makes this safe:
            // a row still referenced by a fuel_vouchers row is a live voucher's provenance and is
            // never matched, however old it is.
            ("qr_parameters", await PurgeAsync("qr_parameters", 
                _context.QrParameters.Where(q =>
                    q.CreatedAtUtc < qrParametersCutoff &&
                    !_context.FuelVouchers.IgnoreQueryFilters()
                        .Any(v => v.QrParametersId == q.Id)),
                dryRun, cancellationToken)),

            // Contact-form submissions past the window. Ordered last so it is the newest policy in the
            // list, and nothing cascades into it - it references no user, so no other table depends on
            // the row surviving.
            ("support_messages", await PurgeAsync("support_messages", 
                _context.SupportMessages.Where(m => m.CreatedAtUtc < supportMessageCutoff),
                dryRun, cancellationToken)),

            // Bot feedback: replies first, because the FK from replies to messages is Restrict and
            // the two tables share one window - deleting the message first would fail the job.
            ("feedback_message_replies", await PurgeAsync("feedback_message_replies", 
                _context.FeedbackMessageReplies.Where(r => r.SentAtUtc < feedbackMessageCutoff),
                dryRun, cancellationToken)),

            ("feedback_messages", await PurgeAsync("feedback_messages", 
                _context.FeedbackMessages.Where(m => m.CreatedAtUtc < feedbackMessageCutoff),
                dryRun, cancellationToken)),
        };

        // __TAIL__
        var total = results.Sum(r => r.Count);
        var breakdown = string.Join(", ", results.Select(r => $"{r.Table}={r.Count}"));

        // A partial run has to say it is partial. The alternative - reporting the tables that were
        // reached and staying quiet about the ones that were not - is how a job that deletes
        // nothing at all can look identical to a healthy one in the log.
        if (_failures.Count > 0)
        {
            var failed = string.Join("; ", _failures.Select(f => $"{f.Table} ({f.Reason})"));
            _logger.LogError(
                "Data retention ran INCOMPLETELY: {FailedCount} table(s) could not be purged and were "
                + "skipped, while {Total} row(s) in the others were deleted [{Breakdown}]. "
                + "Blocked: {Failed}",
                _failures.Count, total, breakdown, failed);

            return;
        }

        if (dryRun)
        {
            _logger.LogInformation(
                "Data retention is OFF (dry-run): {Total} row(s) eligible for purge [{Breakdown}]. " +
                "Set DataRetention:Enabled to delete them.",
                total, breakdown);
        }
        else
        {
            _logger.LogInformation(
                "Data retention purge complete: deleted {Total} row(s) [{Breakdown}]",
                total, breakdown);
        }
    }

    /// <summary>
    /// Dry-run: returns the count of matching rows, deleting nothing. Live: deletes them in bounded
    /// batches (load + <c>RemoveRange</c>) and returns the number removed. The batch query opts into
    /// tracking explicitly - the context tracks by default (see DatabaseSetup.cs on why that
    /// default is load-bearing), and saying it here keeps the delete honest if that ever flips.
    /// </summary>
    /// <remarks>
    /// A failure is contained to the table it happened in. Every delete here is a real constraint the
    /// schema is free to reject - a Restrict edge nobody remembered - and letting that escape
    /// aborted every table listed after it while the job still reported the tables it *had*
    /// reached. That is the worst shape a background job can have: it looks like it ran.
    /// <para>
    /// Not covered by a test, deliberately. Every Restrict edge the purge can currently hit is
    /// excluded by a predicate instead - <c>voucher_imports</c> against <c>purchase_batches</c> and
    /// <c>qr_parameters</c> against <c>fuel_vouchers</c> - so manufacturing a failure would mean
    /// breaking the schema on purpose to assert the handler's own reaction. When a new Restrict edge
    /// appears, add the predicate <em>and</em> a test for it; this block is what stops the first
    /// occurrence from taking the rest of the run with it.
    /// </para>
    /// <para>
    /// Rows already deleted before the failure stay deleted - each batch is its own transaction -
    /// so the table reports what it actually removed rather than rolling back to a tidy zero.
    /// </para>
    /// </remarks>
    private async Task<int> PurgeAsync<T>(
        string table,
        IQueryable<T> filtered,
        bool dryRun,
        CancellationToken cancellationToken) where T : class
    {
        if (dryRun)
        {
            return await filtered.CountAsync(cancellationToken);
        }

        var total = 0;
        while (true)
        {
            var batch = await filtered.AsTracking().Take(BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            try
            {
                _context.Set<T>().RemoveRange(batch);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Almost always a Restrict edge the purge predicate did not account for. Detach the
                // batch so the tracked rows cannot resurface as an insert on some later SaveChanges.
                _context.ChangeTracker.Clear();

                var reason = ex.InnerException?.Message ?? ex.Message;
                _failures.Add((table, reason));
                _logger.LogError(ex,
                    "Data retention could not purge {Table}: {Count} row(s) blocked, and the rest of "
                    + "that batch was skipped. The table stays until the referencing rows are dealt "
                    + "with. {Reason}",
                    table, batch.Count, reason);

                return total;
            }

            total += batch.Count;

            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        return total;
    }
}
