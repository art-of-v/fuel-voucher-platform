using System.Globalization;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers.Renewal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FuelFlow.Features.Settings;

[ApiController]
[Route("api/admin/settings")]
[Authorize(Policy = "Staff")]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly RuntimeSettingsService _settings;

    public AdminSettingsController(RuntimeSettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var enabled = await _settings.IsAutoRefundEnabledAsync(cancellationToken);
        var delayDays = await _settings.GetAutoRefundDelayDaysAsync(cancellationToken);

        var cleanupEnabled = await _settings.IsOrderCleanupEnabledAsync(cancellationToken);
        var cleanupRetentionDays = await _settings.GetOrderCleanupRetentionDaysAsync(cancellationToken);

        var dataRetentionEnabled = await _settings.IsDataRetentionEnabledAsync(cancellationToken);

        var expiredVoucherLossEnabled = await _settings.IsExpiredVoucherLossEnabledAsync(cancellationToken);

        var renewal = await _settings.GetVoucherRenewalConfigAsync(cancellationToken);

        return Ok(new SettingsDto
        {
            AutoRefund = new AutoRefundSettingsDto
            {
                Enabled = enabled,
                DelayDays = delayDays
            },
            OrderCleanup = new OrderCleanupSettingsDto
            {
                Enabled = cleanupEnabled,
                RetentionDays = cleanupRetentionDays
            },
            DataRetention = new DataRetentionSettingsDto
            {
                Enabled = dataRetentionEnabled
            },
            ExpiredVoucherLoss = new ExpiredVoucherLossSettingsDto
            {
                Enabled = expiredVoucherLossEnabled
            },
            VoucherRenewal = new VoucherRenewalSettingsDto
            {
                Enabled = renewal.Enabled,
                TriggerThresholdDays = renewal.TriggerThresholdDays,
                Tiers = renewal.Tiers
                    .Select(t => new VoucherRenewalTierSettingsDto
                    {
                        Term = t.Term.Code(),
                        Enabled = t.Enabled,
                        RatePerLiterUah = t.RatePerLiterUah,
                        Offerable = t.IsOfferable
                    })
                    .ToList()
            }
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.AutoRefund is null && request?.OrderCleanup is null
            && request?.DataRetention is null && request?.ExpiredVoucherLoss is null
            && request?.VoucherRenewal is null)
        {
            return BadRequest(new { success = false, error = "No settings supplied" });
        }

        object? autoRefundResult = null;
        object? orderCleanupResult = null;
        object? dataRetentionResult = null;
        object? expiredVoucherLossResult = null;
        object? voucherRenewalResult = null;

        if (request.AutoRefund is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.AutoRefundEnabled,
                request.AutoRefund.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            var delayDays = Math.Max(1, request.AutoRefund.DelayDays);
            await _settings.UpsertAsync(
                AppSettingKeys.AutoRefundDelayDays,
                delayDays.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            autoRefundResult = new { enabled = request.AutoRefund.Enabled, delayDays };
        }

        if (request.OrderCleanup is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.OrderCleanupEnabled,
                request.OrderCleanup.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            // Clamp to a whole day minimum so a stray 0/negative can never make every abandoned
            // order instantly purgeable; mirrors the job's own Math.Max(1, ...) guard.
            var retentionDays = Math.Max(1, request.OrderCleanup.RetentionDays);
            await _settings.UpsertAsync(
                AppSettingKeys.OrderCleanupRetentionDays,
                retentionDays.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            orderCleanupResult = new { enabled = request.OrderCleanup.Enabled, retentionDays };
        }

        if (request.DataRetention is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.DataRetentionEnabled,
                request.DataRetention.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            dataRetentionResult = new { enabled = request.DataRetention.Enabled };
        }

        if (request.ExpiredVoucherLoss is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.ExpiredVoucherLossEnabled,
                request.ExpiredVoucherLoss.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            expiredVoucherLossResult = new { enabled = request.ExpiredVoucherLoss.Enabled };
        }

        if (request.VoucherRenewal is not null)
        {
            await _settings.UpsertAsync(
                AppSettingKeys.VoucherRenewalEnabled,
                request.VoucherRenewal.Enabled.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            // Clamp the trigger window to a sane whole-day range: at least 1 day (a 0/negative window
            // would never surface the button) and at most a year (a runaway value would offer renewal
            // on vouchers that are nowhere near expiry).
            var thresholdDays = Math.Clamp(request.VoucherRenewal.TriggerThresholdDays, 1, 365);
            await _settings.UpsertAsync(
                AppSettingKeys.VoucherRenewalTriggerThresholdDays,
                thresholdDays.ToString(),
                GetUserId(),
                GetUserName(),
                cancellationToken);

            var tierResults = new List<object>();
            foreach (var tier in request.VoucherRenewal.Tiers ?? new List<VoucherRenewalTierSettingsDto>())
            {
                // Ignore unknown/garbage tier codes rather than persisting orphan keys.
                if (!VoucherRenewalTerms.TryFromCode(tier.Term, out var term))
                {
                    continue;
                }

                var code = term.Code();

                await _settings.UpsertAsync(
                    AppSettingKeys.VoucherRenewalTierEnabled(code),
                    tier.Enabled.ToString(),
                    GetUserId(),
                    GetUserName(),
                    cancellationToken);

                // A price can never be negative; a zero rate is allowed but leaves the tier not-offerable
                // (IsOfferable requires rate > 0), so an admin can pre-enable a tier before pricing it.
                var rate = Math.Max(0m, tier.RatePerLiterUah);
                await _settings.UpsertAsync(
                    AppSettingKeys.VoucherRenewalTierRatePerLiter(code),
                    rate.ToString(CultureInfo.InvariantCulture),
                    GetUserId(),
                    GetUserName(),
                    cancellationToken);

                tierResults.Add(new { term = code, enabled = tier.Enabled, ratePerLiterUah = rate });
            }

            voucherRenewalResult = new
            {
                enabled = request.VoucherRenewal.Enabled,
                triggerThresholdDays = thresholdDays,
                tiers = tierResults
            };
        }

        return Ok(new
        {
            success = true,
            autoRefund = autoRefundResult,
            orderCleanup = orderCleanupResult,
            dataRetention = dataRetentionResult,
            expiredVoucherLoss = expiredVoucherLossResult,
            voucherRenewal = voucherRenewalResult
        });
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
        {
            return $"{first} {last}".Trim();
        }
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}

public sealed class SettingsDto
{
    public AutoRefundSettingsDto AutoRefund { get; set; } = new();
    public OrderCleanupSettingsDto OrderCleanup { get; set; } = new();
    public DataRetentionSettingsDto DataRetention { get; set; } = new();
    public ExpiredVoucherLossSettingsDto ExpiredVoucherLoss { get; set; } = new();
    public VoucherRenewalSettingsDto VoucherRenewal { get; set; } = new();
}

public sealed class AutoRefundSettingsDto
{
    public bool Enabled { get; set; }
    public int DelayDays { get; set; }
}

public sealed class OrderCleanupSettingsDto
{
    public bool Enabled { get; set; }
    public int RetentionDays { get; set; }
}

public sealed class DataRetentionSettingsDto
{
    public bool Enabled { get; set; }
}

public sealed class ExpiredVoucherLossSettingsDto
{
    public bool Enabled { get; set; }
}

public sealed class VoucherRenewalSettingsDto
{
    public bool Enabled { get; set; }
    public int TriggerThresholdDays { get; set; }
    public List<VoucherRenewalTierSettingsDto> Tiers { get; set; } = new();
}

public sealed class VoucherRenewalTierSettingsDto
{
    /// <summary>Stable tier code (e.g. "1w", "3m") — see <see cref="VoucherRenewalTerms"/>.</summary>
    public string Term { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public decimal RatePerLiterUah { get; set; }
    /// <summary>Read-only echo of whether the tier is actually sellable (enabled AND priced). Ignored on write.</summary>
    public bool Offerable { get; set; }
}

public sealed class UpdateSettingsRequest
{
    public AutoRefundSettingsDto? AutoRefund { get; set; }
    public OrderCleanupSettingsDto? OrderCleanup { get; set; }
    public DataRetentionSettingsDto? DataRetention { get; set; }
    public ExpiredVoucherLossSettingsDto? ExpiredVoucherLoss { get; set; }
    public VoucherRenewalSettingsDto? VoucherRenewal { get; set; }
}
