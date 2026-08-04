using FuelFlow.Features.Settings.SharedModels;
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

    public async Task<bool> IsAutoRefundEnabledAsync(CancellationToken cancellationToken = default)
        => await GetBoolAsync(AppSettingKeys.AutoRefundEnabled, defaultValue: false, cancellationToken);

    public async Task<int> GetAutoRefundDelayDaysAsync(CancellationToken cancellationToken = default)
        => await GetIntAsync(AppSettingKeys.AutoRefundDelayDays, DefaultAutoRefundDelayDays, cancellationToken);

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
