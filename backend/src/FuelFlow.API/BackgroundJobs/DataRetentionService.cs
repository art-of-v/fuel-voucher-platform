using FuelFlow.Features.Auth.SharedModels;
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

    // Deliberately NOT covered, and the reason matters more than the list:
    //
    //   voucher_exchanges          - the settlement record. "Who do we settle with, for what, at
    //                                 what cost" is answerable only from these rows, and
    //                                 VOUCHER_LIFECYCLE.md calls the system itself the record
    //                                 (suppliers issue no invoices, there is no paper to check
    //                                 against). Pruning them destroys the audit trail.
    //   voucher_renewal_items      - what the customer paid and how the term moved. CASCADE from
    //                                 orders is the only thing that may remove one.
    //   operator_voucher_renewals  - the same settlement fact for the operator path.
    //   monobank_statement_entries - the acquirer's immutable evidence. Re-import must never
    //                                 rewrite a stored amount (acquiring-statement-118.md), so a
    //                                 prune here would be a silent edit of the ledger.
    //   fuel_vouchers (soft-deleted)- paid-for paper. There is no window during which this is ok.
    //   support_messages           - a user's own words to support, no stated window.

    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly ILogger<DataRetentionService> _logger;

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

        // Devices are purged by TWO predicates rather than the one
        // `Status == Revoked || LastSeenAt < cutoff` they read as, and the reason is the planner,
        // not readability. A `BitmapOr` cannot usefully combine a bare status comparison with an
        // indexed range, so the combined delete stays on a Seq Scan even with an index on
        // LastSeenAt - measured 28.5 ms against 3.7 ms for the split form, at 250k rows.
        //
        // The two sets are disjoint (the second excludes Revoked) and together they match exactly
        // what the OR matched, so the summed count is unchanged - verified by test against the OR.
        var revokedDeviceCount = await PurgeAsync(
            _context.Devices.Where(d => d.Status == DeviceStatus.Revoked),
            dryRun, cancellationToken);

        var staleDeviceCount = await PurgeAsync(
            _context.Devices.Where(d => d.Status != DeviceStatus.Revoked && d.LastSeenAt < deviceCutoff),
            dryRun, cancellationToken);

        // __RESULTS__
        var results = new (string Table, int Count)[]
        {
            // Spent (used) or expired OTPs, aged past the window. A still-valid code — unused and
            // unexpired — is never matched, whatever its age.
            ("verification_codes", await PurgeAsync(
                _context.VerificationCodes.Where(c =>
                    (c.IsUsed || c.ExpiresAtUtc < now) && c.CreatedAtUtc < verificationCutoff),
                dryRun, cancellationToken)),

            // Revoked or expired refresh tokens, aged past the window. An active session's token
            // (unrevoked and unexpired) is excluded regardless of age.
            ("refresh_tokens", await PurgeAsync(
                _context.RefreshTokens.Where(t =>
                    (t.IsRevoked || t.ExpiresAtUtc < now) && t.CreatedAtUtc < refreshTokenCutoff),
                dryRun, cancellationToken)),

            // Only already-processed outbox events, aged by when they were processed. Unprocessed
            // work is never touched.
            ("outbox_events", await PurgeAsync(
                _context.OutboxEvents.Where(o =>
                    o.Processed && o.ProcessedAtUtc != null && o.ProcessedAtUtc < outboxCutoff),
                dryRun, cancellationToken)),

            // Read notifications only; unread ones are kept indefinitely so a user never loses an
            // unseen message to retention.
            ("notifications", await PurgeAsync(
                _context.Notifications.Where(n =>
                    n.IsRead && n.CreatedAtUtc < notificationCutoff),
                dryRun, cancellationToken)),

            // Error logs are pure diagnostics; prune purely by age.
            ("error_logs", await PurgeAsync(
                _context.ErrorLogs.Where(e => e.LoggedAtUtc < errorLogCutoff),
                dryRun, cancellationToken)),

            // Push tokens not seen in the window: a live device refreshes LastSeenAtUtc regularly,
            // so a long-silent token is unreachable and safe to drop (a reinstall mints a new one).
            ("push_tokens", await PurgeAsync(
                _context.PushTokens.Where(p => p.LastSeenAtUtc < pushTokenCutoff),
                dryRun, cancellationToken)),

            // The admin change audit trail, aged purely by when the change happened. There is no
            // processed state to exclude here: a row is a record of something a person did, so the
            // only question is how far back it is still worth answering for.
            ("provider_event_outbox", await PurgeAsync(
                _context.ProviderEventOutbox.Where(e => e.ChangedAtUtc < providerEventCutoff),
                dryRun, cancellationToken)),

            // A revoked device is dead the moment it is revoked, whatever its age; a device the app
            // has not reported from for the window is an uninstalled app. Both are unreachable, and a
            // reinstall registers a fresh device rather than reviving this row. Counted above, in
            // two queries, so the LastSeenAt index is usable.
            ("devices", revokedDeviceCount + staleDeviceCount),

            // A finished import and its error rows, aged by completion. An in-flight import
            // (CompletedAtUtc null) is never matched however old it started. Ordered last so the
            // import errors cascade to their batch in the same run.
            ("voucher_imports", await PurgeAsync(
                _context.VoucherImports.Where(i =>
                    i.CompletedAtUtc != null && i.CompletedAtUtc < voucherImportCutoff),
                dryRun, cancellationToken)),

            // A QR config nothing points at any more. The reference check is what makes this safe:
            // a row still referenced by a fuel_vouchers row is a live voucher's provenance and is
            // never matched, however old it is.
            ("qr_parameters", await PurgeAsync(
                _context.QrParameters.Where(q =>
                    q.CreatedAtUtc < qrParametersCutoff &&
                    !_context.FuelVouchers.IgnoreQueryFilters()
                        .Any(v => v.QrParametersId == q.Id)),
                dryRun, cancellationToken)),
        };

        // __TAIL__
        var total = results.Sum(r => r.Count);
        var breakdown = string.Join(", ", results.Select(r => $"{r.Table}={r.Count}"));

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
    private async Task<int> PurgeAsync<T>(
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

            _context.Set<T>().RemoveRange(batch);
            await _context.SaveChangesAsync(cancellationToken);
            total += batch.Count;

            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        return total;
    }
}
