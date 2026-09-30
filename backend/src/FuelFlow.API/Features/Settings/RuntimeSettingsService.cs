using System.Globalization;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;
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
    public const int DefaultOrderCleanupRetentionDays = 30;
    public const int DefaultVoucherRenewalThresholdDays = 14;

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
    public async Task<bool> IsOrderCleanupEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.OrderCleanupEnabled, defaultValue: false, cancellationToken);

    /// <summary>Retention window (whole days) before an abandoned order is eligible for purge. Defaults to 30.</summary>
    public async Task<int> GetOrderCleanupRetentionDaysAsync(CancellationToken cancellationToken = default)
        => await GetIntAsync(AppSettingKeys.OrderCleanupRetentionDays, DefaultOrderCleanupRetentionDays, cancellationToken);

    /// <summary>Whether the data-retention job may hard-delete operational rows. Fail-safe: defaults to
    /// false, so while off the job only counts (dry-run) and never deletes until an admin opts in.</summary>
    public async Task<bool> IsDataRetentionEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.DataRetentionEnabled, defaultValue: false, cancellationToken);

    /// <summary>Whether the nightly expired-voucher loss-booking job may retire lapsed operator-unsold
    /// vouchers to <c>Expired</c>. Fail-safe: defaults to false, so while off the job only logs the loss
    /// it would book (dry-run) and never mutates voucher status until an admin opts in.</summary>
    public async Task<bool> IsExpiredVoucherLossEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.ExpiredVoucherLossEnabled, defaultValue: false, cancellationToken);

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

        var tiers = VoucherRenewalTerms.All
            .Select(term =>
            {
                var code = term.Code();

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
