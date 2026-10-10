using System.Security.Claims;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.Renewal.Quote;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers.Renewal.Operator;

/// <summary>
/// Staff-only admin endpoints for recording an operator-initiated renewal of a CUSTOMER's voucher
/// (follow-up to #104, which covered only our own stock). Search a customer's renewable voucher, read
/// the standard term ladder, and confirm — the system extends it in place or replaces it from stock,
/// mirroring the mobile flow but with no payment: the operator records money taken off-platform.
/// </summary>
[ApiController]
[Route("api/admin/voucher-renewal")]
[Authorize(Policy = "Staff")]
public sealed class OperatorVoucherRenewalController : ControllerBase
{
    private readonly GetRenewableCustomerVouchersQueryHandler _searchHandler;
    private readonly ConfirmOperatorRenewalCommandHandler _confirmHandler;
    private readonly RuntimeSettingsService _settings;
    private readonly ILogger<OperatorVoucherRenewalController> _logger;

    public OperatorVoucherRenewalController(
        GetRenewableCustomerVouchersQueryHandler searchHandler,
        ConfirmOperatorRenewalCommandHandler confirmHandler,
        RuntimeSettingsService settings,
        ILogger<OperatorVoucherRenewalController> logger)
    {
        _searchHandler = searchHandler;
        _confirmHandler = confirmHandler;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Customer-owned renewable vouchers (search by number / owner, filter by provider + status).</summary>
    [HttpGet("vouchers")]
    [ProducesResponseType(typeof(List<RenewableCustomerVoucherDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchVouchers(
        [FromQuery] string? query,
        [FromQuery] string? provider,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var result = await _searchHandler.HandleAsync(
            new GetRenewableCustomerVouchersQuery(query, provider, status), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// The renewal term ladder for the operator's dropdown — same config the mobile client uses, so the
    /// admin picker matches it (codes + labels; the operator enters the surcharge manually, no pricing).
    /// </summary>
    [HttpGet("terms")]
    [ProducesResponseType(typeof(RenewalConfigResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTerms(CancellationToken cancellationToken)
    {
        var config = await _settings.GetVoucherRenewalConfigAsync(cancellationToken);
        return Ok(RenewalConfigResponse.From(config));
    }

    /// <summary>
    /// Confirms an operator renewal: extends the customer's still-valid voucher in place, or replaces a
    /// lapsed one from stock. Business rejections answer 400 (unknown term / not a customer voucher /
    /// not renewable), 404 (voucher not found) or 409 (no replacement stock) with a stable code.
    /// </summary>
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(ConfirmOperatorRenewalResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmOperatorRenewalCommand command,
        CancellationToken cancellationToken)
    {
        if (command.VoucherId == Guid.Empty)
            return BadRequest(new { code = "voucher_required", message = "VoucherId is required." });

        if (string.IsNullOrWhiteSpace(command.TermCode))
            return BadRequest(new { code = "term_required", message = "TermCode is required." });

        command.ActingUserId = GetUserId();
        command.ActingUserName = GetUserName();

        try
        {
            var result = await _confirmHandler.HandleAsync(command, cancellationToken);
            return Ok(result);
        }
        catch (VoucherRenewalException ex)
        {
            // `data` is what lets a localised client render the numbers in its own words. It is null
            // rather than an empty object for the codes that carry nothing, so a client can tell
            // "no data" from "an empty bag".
            var body = new
            {
                code = ex.Code,
                message = ex.Message,
                data = ex.Data.Count == 0 ? null : ex.Data
            };

            return ex.Code switch
            {
                "not_found" => NotFound(body),
                "no_stock" => Conflict(body),
                _ => BadRequest(body)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming operator renewal for voucher {VoucherId}", command.VoucherId);
            return StatusCode(500, "An error occurred while confirming the renewal");
        }
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (first is not null && last is not null) return $"{first} {last}";
        if (first is not null) return first;
        if (last is not null) return last;
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}
