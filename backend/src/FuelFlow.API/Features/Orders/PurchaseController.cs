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
    /// <summary>Upper bounds on a checkout body. These exist to keep the invoice total in a
    /// sane range and to stop <c>unitPrice * quantity</c> from overflowing a 32-bit int.</summary>
    private const int MaxBulkItems = 50;
    private const int MaxBulkQuantityPerItem = 1000;

    private readonly CreateCheckoutCommandHandler _createCheckoutHandler;
    private readonly BulkCheckoutCommandHandler _bulkCheckoutHandler;
    private readonly GetUserPurchasesCommandHandler _getUserPurchasesHandler;
    private readonly SimulatePaymentCommandHandler _simulatePaymentHandler;
    private readonly ILogger<PurchaseController> _logger;

    public PurchaseController(
        CreateCheckoutCommandHandler createCheckoutHandler,
        BulkCheckoutCommandHandler bulkCheckoutHandler,
        GetUserPurchasesCommandHandler getUserPurchasesHandler,
        SimulatePaymentCommandHandler simulatePaymentHandler,
        ILogger<PurchaseController> logger)
    {
        _createCheckoutHandler = createCheckoutHandler;
        _bulkCheckoutHandler = bulkCheckoutHandler;
        _getUserPurchasesHandler = getUserPurchasesHandler;
        _simulatePaymentHandler = simulatePaymentHandler;
        _logger = logger;
    }

    [HttpPost("bulk")]
    [EnableRateLimiting(PurchasePolicy)]
    [ProducesResponseType(typeof(BulkCheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateBulkPurchase([FromBody] BulkCheckoutCommand command, CancellationToken cancellationToken)
    {
        if (command.Items == null || command.Items.Count == 0)
            return BadRequest("At least one item is required");

        if (command.Items.Count > MaxBulkItems)
            return BadRequest($"A bulk purchase may contain at most {MaxBulkItems} items");

        // Every item must be validated the same way the single-item path validates its body.
        // A negative Quantity here subtracts from the invoice total while fulfilment still
        // hands out vouchers for the positive lines, so this is a money-integrity check.
        foreach (var item in command.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Provider))
                return BadRequest("Provider is required for every item");

            if (string.IsNullOrWhiteSpace(item.FuelTypeId))
                return BadRequest("FuelTypeId is required for every item");

            if (item.Liters <= 0)
                return BadRequest("Liters must be greater than 0 for every item");

            if (item.Quantity <= 0)
                return BadRequest("Quantity must be greater than 0 for every item");

            if (item.Quantity > MaxBulkQuantityPerItem)
                return BadRequest($"Quantity must not exceed {MaxBulkQuantityPerItem} per item");
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return Unauthorized("User ID not found");
        }

        command.UserId = Guid.Parse(userId);

        try
        {
            var response = await _bulkCheckoutHandler.HandleAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating bulk purchase for user {UserId}", userId);
            return StatusCode(500, "An error occurred while creating the purchase");
        }
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

        if (command.Quantity > MaxBulkQuantityPerItem)
            return BadRequest($"Quantity must not exceed {MaxBulkQuantityPerItem}");

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

        command.UserId = Guid.Parse(userId);

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
            var command = new GetUserPurchasesCommand(Guid.Parse(userId));
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
    /// Simulate payment for testing. Development only: this marks an order paid without any
    /// money moving, so in production it is a voucher-minting primitive for anyone holding an
    /// Admin token. Answers 404 outside Development so its existence is not confirmed either.
    /// </summary>
    [HttpPost("simulate")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(SimulatePaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SimulatePayment(
        [FromBody] SimulatePaymentCommand command,
        [FromServices] IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            _logger.LogWarning(
                "Payment simulation attempted outside Development for order {OrderId}",
                command.OrderId);
            return NotFound();
        }

        if (command.OrderId == Guid.Empty)
            return BadRequest("OrderId is required");

        if (command.Scenario != "success" && command.Scenario != "failure")
            return BadRequest("Scenario must be 'success' or 'failure'");

        try
        {
            var response = await _simulatePaymentHandler.HandleAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOrderStateException)
        {
            return Conflict("Order is not in a state that can accept this payment result");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Order not found: {OrderId}", command.OrderId);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating payment for order {OrderId}", command.OrderId);
            return StatusCode(500, "An error occurred while simulating payment");
        }
    }
}