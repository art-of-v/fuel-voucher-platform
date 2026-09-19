using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FuelFlow.Features.Auth.QaTestAccess;

/// <summary>
/// Admin-panel control for QA App-Store test-access. Status is readable by any Staff member;
/// changing the switch requires the stricter <c>QaTestAccessAdmin</c> policy (Admin/ProductOwner),
/// because it turns an authentication mechanism on and off. The QA verification code is never
/// exposed by any action here — only whether a code is configured and whether access is on.
/// </summary>
[ApiController]
[Route("api/admin/qa-test-access")]
[Authorize(Policy = "Staff")]
public sealed class QaTestAccessController : ControllerBase
{
    private readonly RuntimeSettingsService _settings;
    private readonly QaTestAccessService _qaTestAccess;
    private readonly SetQaTestAccessCommandHandler _setHandler;

    public QaTestAccessController(
        RuntimeSettingsService settings,
        QaTestAccessService qaTestAccess,
        SetQaTestAccessCommandHandler setHandler)
    {
        _settings = settings;
        _qaTestAccess = qaTestAccess;
        _setHandler = setHandler;
    }

    [HttpGet]
    [ProducesResponseType(typeof(QaTestAccessStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var setting = await _settings.GetSettingAsync(AppSettingKeys.QaTestAccessEnabled, cancellationToken);
        var enabled = bool.TryParse(setting?.Value, out var parsed) && parsed;

        return Ok(new QaTestAccessStatusDto
        {
            Enabled = enabled,
            // Whether a QA code exists at all. Lets the panel warn that the switch will not work
            // until the deploy secret is set, without ever revealing the code itself.
            Configured = _qaTestAccess.IsConfigured,
            PhoneNumber = _qaTestAccess.QaPhoneNumber,
            UpdatedAtUtc = setting?.UpdatedAtUtc,
            UpdatedByUserName = setting?.UpdatedByUserName
        });
    }

    [HttpPut]
    [Authorize(Policy = "QaTestAccessAdmin")]
    [ProducesResponseType(typeof(QaTestAccessStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateQaTestAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { success = false, error = "No value supplied" });

        var result = await _setHandler.HandleAsync(
            new SetQaTestAccessCommand(request.Enabled, GetUserId(), GetUserName(), GetRole()),
            cancellationToken);

        var setting = await _settings.GetSettingAsync(AppSettingKeys.QaTestAccessEnabled, cancellationToken);

        return Ok(new QaTestAccessStatusDto
        {
            Enabled = result.Enabled,
            Configured = _qaTestAccess.IsConfigured,
            PhoneNumber = _qaTestAccess.QaPhoneNumber,
            UpdatedAtUtc = setting?.UpdatedAtUtc,
            UpdatedByUserName = setting?.UpdatedByUserName
        });
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var parsed) ? parsed : Guid.Empty;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
            return $"{first} {last}".Trim();
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }

    private string? GetRole() =>
        User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value;
}

public sealed class QaTestAccessStatusDto
{
    public bool Enabled { get; set; }
    public bool Configured { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedByUserName { get; set; }
}

public sealed class UpdateQaTestAccessRequest
{
    public bool Enabled { get; set; }
}
