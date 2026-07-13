using System.Security.Claims;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.SharedKernel.DTOs;
using FuelFlow.Features.Orders.SimulatePayment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static FuelFlow.API.Extensions.RateLimiterSetup;

namespace FuelFlow.API.Features.Orders;

[ApiController]
[Route("api/purchases")]
[Authorize]
public sealed class PurchaseController : ControllerBase
{
    private readonly CreateCheckoutCommandHandler _createCheckoutHandler;
    private readonly GetUserPurchasesCommandHandler _getUserPurchasesHandler;
    private readonly SimulatePaymentCommandHandler _simulatePaymentHandler;
    private readonly ILogger<PurchaseController> _logger;

    public PurchaseController(
        CreateCheckoutCommandHandler createCheckoutHandler,
        GetUserPurchasesCommandHandler getUserPurchasesHandler,
        SimulatePaymentCommandHandler simulatePaymentHandler,
        ILogger<PurchaseController> logger)
    {
        _createCheckoutHandler = createCheckoutHandler;
        _getUserPurchasesHandler = getUserPurchasesHandler;
        _simulatePaymentHandler = simulatePaymentHandler;
        _logger = logger;
    }

    [HttpPost]
    [EnableRateLimiting(PurchasePolicy)]
    [ProducesResponseType(typeof(CreateCheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreatePurchase([FromBody] CreateCheckoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Provider))
            return BadRequest("Provider is required");

        if (string.IsNullOrWhiteSpace(command.FuelTypeId))
            return BadRequest("FuelTypeId is required");

        if (command.Liters <= 0)
            return BadRequest("Liters must be greater than 0");

        if (command.Quantity <= 0)
            return BadRequest("Quantity must be greater than 0");

        if (command.Price <= 0)
            return BadRequest("Price must be greater than 0");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return Unauthorized("User ID not found");
        }

        command.UserId = userId;

        try
        {
            var response = await _createCheckoutHandler.HandleAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating purchase for user {UserId}", userId);
            return StatusCode(500, "An error occurred while creating the purchase");
        }
    }

    [HttpGet("my")]
    [ProducesResponseType(typeof(List<PurchaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyPurchases(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return Unauthorized("User ID not found");
        }

        try
        {
            var command = new GetUserPurchasesCommand(userId);
            var purchases = await _getUserPurchasesHandler.HandleAsync(command, cancellationToken);
            return Ok(purchases);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving purchases for user {UserId}", userId);
            return StatusCode(500, "An error occurred while retrieving purchases");
        }
    }

    /// <summary>
    /// Simulate payment for testing (development only)
    /// </summary>
    [HttpPost("simulate")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SimulatePaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SimulatePayment(
        [FromBody] SimulatePaymentCommand command,
        CancellationToken cancellationToken)
    {
        if (command.OrderId == Guid.Empty)
            return BadRequest("OrderId is required");

        if (command.Scenario != "success" && command.Scenario != "failure")
            return BadRequest("Scenario must be 'success' or 'failure'");

        try
        {
            var response = await _simulatePaymentHandler.HandleAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Order not found: {OrderId}", command.OrderId);
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating payment for order {OrderId}", command.OrderId);
            return StatusCode(500, "An error occurred while simulating payment");
        }
    }
}
