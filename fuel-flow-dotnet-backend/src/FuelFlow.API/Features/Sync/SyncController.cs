using System.Security.Claims;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Sync.GetSync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Sync;

[ApiController]
[Route("api/sync")]
[Authorize]
public sealed class SyncController : ControllerBase
{
    private readonly GetSyncCommandHandler _getSyncHandler;
    private readonly GetUserPurchasesCommandHandler _getUserPurchasesHandler;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        GetSyncCommandHandler getSyncHandler,
        GetUserPurchasesCommandHandler getUserPurchasesHandler,
        ILogger<SyncController> logger)
    {
        _getSyncHandler = getSyncHandler;
        _getUserPurchasesHandler = getUserPurchasesHandler;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetSyncResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
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
            var command = new GetSyncCommand(userId);
            var response = await _getSyncHandler.HandleAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing data for user {UserId}", userId);
            return StatusCode(500, "An error occurred while syncing data");
        }
    }

    [HttpGet("orders")]
    [ProducesResponseType(typeof(List<FuelFlow.Features.Orders.SharedModels.PurchaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrders(CancellationToken cancellationToken)
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
            var command = new GetUserPurchasesCommand(userId);
            var orders = await _getUserPurchasesHandler.HandleAsync(command, cancellationToken);
            return Ok(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orders for user {UserId}", userId);
            return StatusCode(500, "An error occurred while retrieving orders");
        }
    }
}
