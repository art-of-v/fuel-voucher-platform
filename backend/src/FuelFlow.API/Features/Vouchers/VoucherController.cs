using System.Security.Claims;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.GetUserVouchers;
using FuelFlow.Features.Vouchers.MarkVoucherAsUsed;
using FuelFlow.Features.Vouchers.RestoreVoucher;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Errors;
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
    public async Task<IActionResult> GetMyVouchers(
        [FromQuery] int page = 1,
        // The default page size is the MAXIMUM, not a smaller number, and that is deliberate.
        //
        // The app that is in everybody's hands today asks for no page at all, and it is the
        // one client that cannot be changed in the same deploy: the mobile app reaches its
        // users through the store and through an OTA, and the backend reaches them now. A
        // smaller default would truncate a customer's wallet in the window between the two,
        // and the missing vouchers would be SILENT - a wallet that shows fewer vouchers
        // than the customer owns is a worse failure than a slow response, because a slow
        // response is visible and a missing voucher is not.
        //
        // The app that DOES ask for pages asks for 50, and it is the one that pays the
        // price of a small page: one request, one page, one set of rendered QR images.
        [FromQuery] int pageSize = PageLimits.MaxPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return ApiError.Unauthorized("User ID not found");
        }

        try
        {
            // The page size is clamped by PagedRequest, so a client asking for
            // "?pageSize=2000000000" gets the maximum and not the whole table.
            var command = new GetUserVouchersCommand(
                Guid.Parse(userId),
                new PagedRequest(Page: page, PageSize: pageSize));
            var response = await _getUserVouchersHandler.HandleAsync(command, cancellationToken);

            // The body stays a plain array - the mobile client reads an array and has for
            // as long as this endpoint has existed - and the count travels in a header, so
            // a client can tell that another page follows without the shape changing.
            Response.Headers["X-Total-Count"] = response.TotalCount.ToString();
            Response.Headers["X-Has-Next-Page"] = response.HasNextPage ? "true" : "false";
            return Ok(response.Vouchers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting vouchers for user {UserId}", userId);
            return ApiError.Result(StatusCodes.Status500InternalServerError, ApiError.UnexpectedError);
        }
    }
    [HttpGet("inventory")]
    [Authorize(Policy = "Staff")]
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
            return ApiError.Result(StatusCodes.Status500InternalServerError, ApiError.UnexpectedError);
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
            return ApiError.Unauthorized("User ID not found");
        }

        try
        {
            var command = new MarkVoucherAsUsedCommand(id, Guid.Parse(userId));
            var response = await _markAsUsedHandler.HandleAsync(command, cancellationToken);

            if (!response.Success)
            {
                return response.ErrorCode switch
                {
                    "NotFound" => ApiError.NotFound(response.Message),
                    "Forbidden" => Forbid(),
                    _ => ApiError.BadRequest(response.Message)
                };
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking voucher {VoucherId} as used for user {UserId}", id, userId);
            return ApiError.Result(StatusCodes.Status500InternalServerError, ApiError.UnexpectedError);
        }
    }

    [HttpPatch("{id}/restore")]
    [Authorize]
    [ProducesResponseType(typeof(RestoreVoucherResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreVoucher([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        // Was [Authorize(Policy = "Staff")], but the only caller is the mobile client's "undo my
        // redemption" toggle (useMyCodes.ts) - a customer action that could only ever return 403.
        // Ownership is enforced in the handler instead, exactly as for mark-used.
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return ApiError.Unauthorized("User ID not found");
        }

        try
        {
            var command = new RestoreVoucherCommand(id, Guid.Parse(userId));
            var response = await _restoreHandler.HandleAsync(command, cancellationToken);

            if (!response.Success)
            {
                return response.ErrorCode switch
                {
                    "NotFound" => ApiError.NotFound(response.Message),
                    "Forbidden" => Forbid(),
                    _ => ApiError.BadRequest(response.Message)
                };
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring voucher {VoucherId}", id);
            return ApiError.Result(StatusCodes.Status500InternalServerError, ApiError.UnexpectedError);
        }
    }
}
