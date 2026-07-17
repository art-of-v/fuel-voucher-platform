using System.Security.Claims;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/vouchers")]
public sealed class VoucherController : ControllerBase
{
    private readonly GetUserVouchersCommandHandler _getUserVouchersHandler;
    private readonly GetInventoryCommandHandler _getInventoryHandler;
    private readonly MarkVoucherAsUsedCommandHandler _markAsUsedHandler;
    private readonly RestoreVoucherCommandHandler _restoreHandler;
    private readonly ILogger<VoucherController> _logger;

    public VoucherController(
        GetUserVouchersCommandHandler getUserVouchersHandler,
        GetInventoryCommandHandler getInventoryHandler,
        MarkVoucherAsUsedCommandHandler markAsUsedHandler,
        RestoreVoucherCommandHandler restoreHandler,
        ILogger<VoucherController> logger)
    {
        _getUserVouchersHandler = getUserVouchersHandler;
        _getInventoryHandler = getInventoryHandler;
        _markAsUsedHandler = markAsUsedHandler;
        _restoreHandler = restoreHandler;
        _logger = logger;
    }

    [HttpGet("my")]
    [Authorize]
    [ProducesResponseType(typeof(GetUserVouchersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyVouchers(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return Unauthorized("User ID not found");
        }

        try
        {
            var command = new GetUserVouchersCommand(userId);
            var response = await _getUserVouchersHandler.HandleAsync(command, cancellationToken);
            return Ok(response.Vouchers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting vouchers for user {UserId}", userId);
            return StatusCode(500, "An error occurred while retrieving vouchers");
        }
    }

    [HttpGet("inventory")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(GetInventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInventory(CancellationToken cancellationToken)
    {
        try
        {
            var command = new GetInventoryCommand();
            var response = await _getInventoryHandler.HandleAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting voucher inventory");
            return StatusCode(500, "An error occurred while retrieving inventory");
        }
    }

    [HttpPatch("{id}/mark-used")]
    [Authorize]
    [ProducesResponseType(typeof(MarkVoucherAsUsedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkVoucherAsUsed([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return Unauthorized("User ID not found");
        }

        try
        {
            var command = new MarkVoucherAsUsedCommand(id, userId);
            var response = await _markAsUsedHandler.HandleAsync(command, cancellationToken);

            if (!response.Success)
            {
                return BadRequest(response.Message);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking voucher {VoucherId} as used for user {UserId}", id, userId);
            return StatusCode(500, "An error occurred while marking the voucher as used");
        }
    }

    [HttpPatch("{id}/restore")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(RestoreVoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RestoreVoucher([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var command = new RestoreVoucherCommand(id);
            var response = await _restoreHandler.HandleAsync(command, cancellationToken);

            if (!response.Success)
            {
                return BadRequest(response.Message);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring voucher {VoucherId}", id);
            return StatusCode(500, "An error occurred while restoring the voucher");
        }
    }
}
