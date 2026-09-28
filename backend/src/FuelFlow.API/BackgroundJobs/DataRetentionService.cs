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
/// their irreversible purge is a separate switch (see <see cref="OrderCleanupService"/>).
///
/// Deletes are batched (load + <c>RemoveRange</c>, mirroring <see cref="OrderCleanupService"/>): each
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
    /// batches (load + <c>RemoveRange</c>) and returns the number removed. The context defaults to
    /// NoTracking, so the batch query opts into tracking to let <c>RemoveRange</c> stage the delete.
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
