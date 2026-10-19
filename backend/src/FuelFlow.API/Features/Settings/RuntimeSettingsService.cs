using System.Globalization;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Terms;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Settings;

/// <summary>
/// Reads and writes the runtime <c>app_settings</c> table. Missing rows fall back to the
/// supplied defaults so the system keeps working even before the seed migration runs.
/// </summary>
public sealed class RuntimeSettingsService
{
    public const int DefaultAutoRefundDelayDays = 7;
    public const int DefaultDeletedUnpaidOrderCleanupRetentionDays = 30;
    public const int DefaultVoucherRenewalThresholdDays = 14;
    public const int DefaultExpiredVoucherLossGraceDays = 14;
    public const int DefaultHangfireCleanupRetentionDays = 7;

    private readonly ApplicationDbContext _context;

    public RuntimeSettingsService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _context.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        return setting?.Value;
    }

    /// <summary>The full setting row (including who changed it and when), or null if never set.
    /// Used by admin status views that surface the last-changed metadata.</summary>
    public async Task<AppSetting?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
        => await _context.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

    public async Task<bool> GetBoolAsync(
        string key,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, cancellationToken);
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    public async Task<int> GetIntAsync(
        string key,
        int defaultValue,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, cancellationToken);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    public async Task<decimal> GetDecimalAsync(
        string key,
        decimal defaultValue,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, cancellationToken);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    public async Task<bool> IsAutoRefundEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.AutoRefundEnabled, defaultValue: false, cancellationToken);

    public async Task<int> GetAutoRefundDelayDaysAsync(CancellationToken cancellationToken = default)
        => await GetIntAsync(AppSettingKeys.AutoRefundDelayDays, DefaultAutoRefundDelayDays, cancellationToken);

    /// <summary>
    /// Whether the QA test-access runtime switch is on. Fail-safe: a missing row, a value that is
    /// not exactly "true"/"false", or any read failure all resolve to <c>false</c> (via
    /// <see cref="GetBoolAsync"/>'s default and the caller's try/catch), so QA access never
    /// defaults on. This reads the <c>app_settings</c> row fresh on every call (no cache), so an
    /// admin turning it off takes effect on the very next authentication attempt.
    /// </summary>
    public async Task<bool> IsQaTestAccessEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.QaTestAccessEnabled, defaultValue: false, cancellationToken);

    /// <summary>Whether the abandoned-order cleanup job may hard-delete rows. Fail-safe: defaults to
    /// false, so an irreversible purge never runs until an admin explicitly turns it on.</summary>
    public async Task<bool> IsDeletedUnpaidOrderCleanupEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.DeletedUnpaidOrderCleanupEnabled, defaultValue: false, cancellationToken);

    /// <summary>Retention window (whole days) before an abandoned order is eligible for purge. Defaults to 30.</summary>
    public async Task<int> GetDeletedUnpaidOrderCleanupRetentionDaysAsync(CancellationToken cancellationToken = default)
        => await GetIntAsync(AppSettingKeys.DeletedUnpaidOrderCleanupRetentionDays, DefaultDeletedUnpaidOrderCleanupRetentionDays, cancellationToken);

    /// <summary>Whether the data-retention job may hard-delete operational rows. Fail-safe: defaults to
    /// false, so while off the job only counts (dry-run) and never deletes until an admin opts in.</summary>
    public async Task<bool> IsDataRetentionEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.DataRetentionEnabled, defaultValue: false, cancellationToken);

    /// <summary>Whether the nightly expired-voucher loss-booking job may retire lapsed operator-unsold
    /// vouchers to <c>Expired</c>. Fail-safe: defaults to false, so while off the job only logs the loss
    /// it would book (dry-run) and never mutates voucher status until an admin opts in.</summary>
    public async Task<bool> IsExpiredVoucherLossEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.ExpiredVoucherLossEnabled, defaultValue: false, cancellationToken);

    /// <summary>How many days after the printed expiration date the loss-booking job should wait
    /// before retiring a voucher. Defaults to 14 (two weeks), matching the renewal trigger threshold.
    /// A value of 0 means the legacy behaviour — loss booked on the printed expiry date.</summary>
    public async Task<int> GetExpiredVoucherLossGraceDaysAsync(CancellationToken cancellationToken = default)
        => await GetIntAsync(AppSettingKeys.ExpiredVoucherLossGraceDays, DefaultExpiredVoucherLossGraceDays, cancellationToken);

    /// <summary>
    /// Whether the nightly Hangfire backstop may remove the finished-job history that Hangfire's own
    /// expiration manager should have taken and did not. Defaults to <b>true</b>, which is the
    /// opposite of every other destructive switch in this class, and the reason is what the rows are:
    /// the scheduler's own bookkeeping about work that already completed. No money, no customer and
    /// no settlement is in any of them, and a manager that has quietly stopped is a failure nothing
    /// else would notice.
    /// <para>
    /// The default also decides what a malformed value does: <see cref="GetBoolAsync"/> falls back to
    /// the default when the value does not parse, so a typo here leaves the backstop <em>running</em>
    /// rather than silently stopping it. The only text that turns it off is the exact word
    /// <c>false</c>.
    /// </para>
    /// </summary>
    public async Task<bool> IsHangfireCleanupEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.HangfireCleanupEnabled, defaultValue: true, cancellationToken);

    /// <summary>The hard bound on how long a finished job's history is kept, in whole days. Defaults
    /// to 7.</summary>
    public async Task<int> GetHangfireCleanupRetentionDaysAsync(CancellationToken cancellationToken = default)
        => await GetIntAsync(AppSettingKeys.HangfireCleanupRetentionDays, DefaultHangfireCleanupRetentionDays, cancellationToken);

    /// <summary>Whether the in-app feedback channel (Telegram bot) is offered to customers.
    /// Fail-safe: defaults to false, so the bottom-bar tab does not appear until a manager turns
    /// it on. Read fresh on every call, so turning it off takes effect on the next request rather
    /// than at the next deploy.</summary>
    public async Task<bool> IsFeedbackBotEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.FeedbackBotEnabled, defaultValue: false, cancellationToken);

    /// <summary>Whether the paid voucher renewal/replacement flow is live. Fail-safe: defaults to false,
    /// so nothing renews until a manager turns it on and configures tier prices.</summary>
    public async Task<bool> IsVoucherRenewalEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.VoucherRenewalEnabled, defaultValue: false, cancellationToken);

    /// <summary>Remaining-validity window (whole days) that surfaces the renew/replace button. Defaults to 14.</summary>
    public async Task<int> GetVoucherRenewalThresholdDaysAsync(CancellationToken cancellationToken = default)
        => await GetIntAsync(AppSettingKeys.VoucherRenewalTriggerThresholdDays, DefaultVoucherRenewalThresholdDays, cancellationToken);

    /// <summary>
    /// Loads the whole renewal configuration in a single query. Missing rows fall back to fail-safe
    /// defaults (feature off, 14-day threshold, every tier off with a zero rate ⇒ not offerable).
    /// </summary>
    public async Task<VoucherRenewalConfig> GetVoucherRenewalConfigAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.AppSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith(AppSettingKeys.VoucherRenewalPrefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var enabled = rows.TryGetValue(AppSettingKeys.VoucherRenewalEnabled, out var enabledRaw)
            && bool.TryParse(enabledRaw, out var enabledParsed) && enabledParsed;

        var thresholdDays = rows.TryGetValue(AppSettingKeys.VoucherRenewalTriggerThresholdDays, out var thresholdRaw)
            && int.TryParse(thresholdRaw, out var thresholdParsed)
                ? thresholdParsed
                : DefaultVoucherRenewalThresholdDays;

        var currentRows = await LoadCurrentTiersAsync(VoucherTermPricingSchemes.RenewalRate, cancellationToken);

        var tiers = VoucherRenewalTerms.All
            .Select(term =>
            {
                var code = term.Code();

                // The dated row wins when there is one; the key/value setting is the fallback.
                // Falling back rather than replacing outright is what makes deploy order irrelevant:
                // a tier with no row yet prices exactly as it did before the table existed.
                var current = currentRows
                    .FirstOrDefault(r => r.TermCode == code);

                if (current is not null)
                {
                    return new VoucherRenewalTierConfig(term, current.Enabled, current.RatePerLiter);
                }

                var tierEnabled = rows.TryGetValue(AppSettingKeys.VoucherRenewalTierEnabled(code), out var tierEnabledRaw)
                    && bool.TryParse(tierEnabledRaw, out var tierEnabledParsed) && tierEnabledParsed;

                var rate = rows.TryGetValue(AppSettingKeys.VoucherRenewalTierRatePerLiter(code), out var rateRaw)
                    && decimal.TryParse(rateRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var rateParsed)
                        ? rateParsed
                        : 0m;

                return new VoucherRenewalTierConfig(term, tierEnabled, rate);
            })
            .ToList();

        return new VoucherRenewalConfig(enabled, thresholdDays, tiers);
    }

    /// <summary>
    /// Loads the whole term-sale configuration in a single query. Missing rows fall back to fail-safe
    /// defaults (feature off, every tier off with a zero discount), which preserves the pre-existing
    /// behaviour of selling the voucher's full remaining term at the undiscounted price.
    /// </summary>
    public async Task<VoucherTermConfig> GetVoucherTermConfigAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _context.AppSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith(AppSettingKeys.VoucherTermPrefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        var enabled = rows.TryGetValue(AppSettingKeys.VoucherTermSaleEnabled, out var enabledRaw)
            && bool.TryParse(enabledRaw, out var enabledParsed) && enabledParsed;

        var currentRows = await LoadCurrentTiersAsync(VoucherTermPricingSchemes.PurchaseTermDiscount, cancellationToken);

        var tiers = VoucherRenewalTerms.All
            .Select(term =>
            {
                var code = term.Code();

                var current = currentRows
                    .FirstOrDefault(r => r.TermCode == code);

                if (current is not null)
                {
                    return new VoucherTermTierConfig(term, current.Enabled, current.RatePerLiter);
                }

                var tierEnabled = rows.TryGetValue(AppSettingKeys.VoucherTermTierEnabled(code), out var tierEnabledRaw)
                    && bool.TryParse(tierEnabledRaw, out var tierEnabledParsed) && tierEnabledParsed;

                var discount = rows.TryGetValue(AppSettingKeys.VoucherTermTierDiscountPerLiter(code), out var discountRaw)
                    && decimal.TryParse(discountRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var discountParsed)
                        ? discountParsed
                        : 0m;

                return new VoucherTermTierConfig(term, tierEnabled, discount);
            })
            .ToList();

        return new VoucherTermConfig(enabled, tiers);
    }

    /// <summary>
    /// The open row for every tier of one scheme, i.e. the prices in force right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One query per ladder, on the quote and checkout paths, so it is deliberately narrow: only
    /// <c>valid_to IS NULL</c> rows, projected to the three fields the tiers need. History stays in
    /// the table but is not paid for on every price.
    /// </para>
    /// <para>
    /// The window is expressed as "not closed yet" rather than "valid_from &lt;= now", because the
    /// only writer opens a row stamped with the same clock it closes the previous one. Comparing to
    /// <c>now</c> as well would make a change invisible for the microseconds between the two
    /// writes, which is a much rarer bug to hit than a clock skew but a much worse one to explain.
    /// </para>
    /// </remarks>
    private async Task<List<VoucherTermPricing>> LoadCurrentTiersAsync(
        string scheme,
        CancellationToken cancellationToken)
    {
        return await _context.VoucherTermPricings
            .AsNoTracking()
            .Where(r => r.Scheme == scheme && r.ValidTo == null)
            .Select(r => new VoucherTermPricing
            {
                Id = r.Id,
                Scheme = r.Scheme,
                TermCode = r.TermCode,
                Enabled = r.Enabled,
                RatePerLiter = r.RatePerLiter,
                ValidFrom = r.ValidFrom,
                ValidTo = r.ValidTo
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Sets one tier, keeping the history: the open row is closed and a new one opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An unchanged tier writes nothing.</b> The Settings screen saves the whole ladder in one
    /// request, so without this every save of an unrelated setting would append sixteen identical rows
    /// and the history would be unreadable - which is the only reason to keep history at all.
    /// </para>
    /// <para>
    /// <b>Both writes are in one transaction.</b> Closing the old row and opening the new one
    /// separately would pass through a moment where the tier has no current price; if the process
    /// died there, the partial unique index would then refuse the retry.
    /// </para>
    /// </remarks>
    /// <returns>True when a row was appended; false when the tier was already at these values.</returns>
    public async Task<bool> SetTierAsync(
        string scheme,
        string termCode,
        bool enabled,
        decimal ratePerLiter,
        Guid? updatedByUserId,
        string? updatedByUserName,
        CancellationToken cancellationToken = default)
    {
        var open = await _context.VoucherTermPricings
            .AsTracking()
            .FirstOrDefaultAsync(
                r => r.Scheme == scheme && r.TermCode == termCode && r.ValidTo == null,
                cancellationToken);

        if (open is not null && open.Enabled == enabled && open.RatePerLiter == ratePerLiter)
        {
            return false;
        }

        var now = DateTime.UtcNow;

        if (open is not null)
        {
            open.ValidTo = now;
            open.UpdatedAtUtc = now;
            open.UpdatedByUserId = updatedByUserId;
            open.UpdatedByUserName = updatedByUserName;
        }

        _context.VoucherTermPricings.Add(new VoucherTermPricing
        {
            Id = Guid.NewGuid(),
            Scheme = scheme,
            TermCode = termCode,
            Enabled = enabled,
            RatePerLiter = ratePerLiter,
            ValidFrom = now,
            ValidTo = null,
            CreatedAtUtc = now,
            UpdatedByUserId = updatedByUserId,
            UpdatedByUserName = updatedByUserName,
            UpdatedAtUtc = now
        });

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task UpsertAsync(
        string key,
        string value,
        Guid? updatedByUserId = null,
        string? updatedByUserName = null,
        CancellationToken cancellationToken = default)
    {
        // The context is configured with a global NoTracking behavior, so explicitly track
        // the row here. That lets repeated upserts in the same scoped context reuse the same
        // tracked instance instead of tripping the identity map.
        var setting = await _context.AppSettings
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting is null)
        {
            _context.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedByUserId = updatedByUserId,
                UpdatedByUserName = updatedByUserName
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAtUtc = DateTime.UtcNow;
            setting.UpdatedByUserId = updatedByUserId;
            setting.UpdatedByUserName = updatedByUserName;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
