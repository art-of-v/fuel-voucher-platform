using FuelFlow.Features.Settings.SharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FuelFlow.Features.Settings;

[ApiController]
[Route("api/admin/settings")]
[Authorize(Roles = "Admin")]
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

        return Ok(new SettingsDto
        {
            AutoRefund = new AutoRefundSettingsDto
            {
                Enabled = enabled,
                DelayDays = delayDays
            }
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.AutoRefund is null)
        {
            return BadRequest(new { success = false, error = "No settings supplied" });
        }

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

        return Ok(new { success = true, autoRefund = new { enabled = request.AutoRefund.Enabled, delayDays } });
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
}

public sealed class AutoRefundSettingsDto
{
    public bool Enabled { get; set; }
    public int DelayDays { get; set; }
}

public sealed class UpdateSettingsRequest
{
    public AutoRefundSettingsDto? AutoRefund { get; set; }
}
