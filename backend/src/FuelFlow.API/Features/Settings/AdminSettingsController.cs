using FuelFlow.Features.Settings.SharedModels;
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
            }
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.AutoRefund is null && request?.OrderCleanup is null)
        {
            return BadRequest(new { success = false, error = "No settings supplied" });
        }

        object? autoRefundResult = null;
        object? orderCleanupResult = null;

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

        return Ok(new { success = true, autoRefund = autoRefundResult, orderCleanup = orderCleanupResult });
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

public sealed class UpdateSettingsRequest
{
    public AutoRefundSettingsDto? AutoRefund { get; set; }
    public OrderCleanupSettingsDto? OrderCleanup { get; set; }
}
