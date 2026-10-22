using System.Security.Claims;
using FuelFlow.Features.Settings.GetAdminSettings;
using FuelFlow.Features.Settings.RegisterFeedbackWebhook;
using FuelFlow.Features.Settings.UpdateAdminSettings;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Settings;

[ApiController]
[Route("api/admin/settings")]
public sealed class AdminSettingsController : ControllerBase
{
    private readonly GetAdminSettingsQueryHandler _getAll;
    private readonly UpdateAdminSettingsCommandHandler _update;
    private readonly RegisterFeedbackWebhookCommandHandler _registerWebhook;

    public AdminSettingsController(
        GetAdminSettingsQueryHandler getAll,
        UpdateAdminSettingsCommandHandler update,
        RegisterFeedbackWebhookCommandHandler registerWebhook)
    {
        _getAll = getAll;
        _update = update;
        _registerWebhook = registerWebhook;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await _getAll.HandleAsync(new GetAdminSettingsQuery(), cancellationToken));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.SettingsWrite)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _update.HandleAsync(
            new UpdateAdminSettingsCommand(request, GetUserId(), GetUserName()),
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            autoRefund = result.AutoRefund,
            deletedUnpaidOrderCleanup = result.DeletedUnpaidOrderCleanup,
            dataRetention = result.DataRetention,
            expiredVoucherLoss = result.ExpiredVoucherLoss,
            voucherRenewal = result.VoucherRenewal,
            voucherTerm = result.VoucherTerm,
            feedbackBot = result.FeedbackBot
        });
    }

    /// <summary>
    /// Registers the feedback webhook against this deployment's own public address, so an operator
    /// does not have to hand-assemble the URL and paste it into a terminal. The public origin is
    /// read off the incoming request rather than configured, because it differs per environment
    /// (localhost tunnel in development, the API domain behind the proxy in production) and a
    /// misconfigured value would point Telegram at a host that answers 404 for every update.
    /// </summary>
    [HttpPost("feedback/webhook")]
    [Authorize(Policy = AuthPolicies.SettingsWrite)]
    public async Task<IActionResult> RegisterFeedbackWebhook(CancellationToken cancellationToken)
    {
        var url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/telegram/feedback/webhook";

        var result = await _registerWebhook.HandleAsync(
            new RegisterFeedbackWebhookCommand(url),
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new { success = true, url, webhook = result.Webhook });
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
