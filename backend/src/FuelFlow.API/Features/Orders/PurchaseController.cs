using System.Security.Claims;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.DeleteMyOrder;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.GetSavingsReport;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.DTOs;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.Renewal.Quote;
using FuelFlow.Features.Settings;
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
    private readonly RenewalCheckoutCommandHandler _renewalCheckoutHandler;
    private readonly RenewalQuoteCommandHandler _renewalQuoteHandler;
    private readonly TermQuoteQueryHandler _termQuoteHandler;
    private readonly RuntimeSettingsService _settings;
    private readonly GetUserPurchasesCommandHandler _getUserPurchasesHandler;
    private readonly GetSavingsReportQueryHandler _getSavingsReportHandler;
    private readonly SimulatePaymentCommandHandler _simulatePaymentHandler;
    private readonly DeleteMyOrderCommandHandler _deleteMyOrderHandler;
    private readonly ILogger<PurchaseController> _logger;

    public PurchaseController(
        CreateCheckoutCommandHandler createCheckoutHandler,
        BulkCheckoutCommandHandler bulkCheckoutHandler,
        RenewalCheckoutCommandHandler renewalCheckoutHandler,
RenewalQuoteCommandHandler renewalQuoteHandler,
    TermQuoteQueryHandler termQuoteHandler,
    RuntimeSettingsService settings,
        GetUserPurchasesCommandHandler getUserPurchasesHandler,
        GetSavingsReportQueryHandler getSavingsReportHandler,
        SimulatePaymentCommandHandler simulatePaymentHandler,
        DeleteMyOrderCommandHandler deleteMyOrderHandler,
        ILogger<PurchaseController> logger)
    {
        _createCheckoutHandler = createCheckoutHandler;
        _bulkCheckoutHandler = bulkCheckoutHandler;
        _renewalCheckoutHandler = renewalCheckoutHandler;
        _renewalQuoteHandler = renewalQuoteHandler;
    _termQuoteHandler = termQuoteHandler;
        _settings = settings;
        _getUserPurchasesHandler = getUserPurchasesHandler;
        _getSavingsReportHandler = getSavingsReportHandler;
        _simulatePaymentHandler = simulatePaymentHandler;
        _deleteMyOrderHandler = deleteMyOrderHandler;
        _logger = logger;
    }

    [HttpPost("bulk")]
    [EnableRateLimiting(PurchasePolicy)]
    [ProducesResponseType(typeof(BulkCheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
        catch (BelowCostSaleBlockedException ex)
        {
            return Conflict(new { code = BelowCostSaleBlockedException.Code, message = ex.Message });
        }
        catch (TermStockUnavailableException ex)
        {
            return Conflict(new { code = TermStockUnavailableException.Code, message = ex.Message });
        }
        catch (AccountInactiveException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = AccountInactiveException.Code, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating bulk purchase for user {UserId}", userId);
            return StatusCode(500, "An error occurred while creating the purchase");
        }
    }

    /// <summary>
    /// The renewal feature config the mobile client needs to gate the "renew" affordance before any
    /// voucher is picked: whether the feature is on, the trigger window in days, and the tier ladder
    /// with per-litre rates + offerable flags. Concrete per-voucher prices come from <c>renew/quote</c>.
    /// </summary>
    [HttpGet("renew/config")]
    [ProducesResponseType(typeof(RenewalConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRenewalConfig(CancellationToken cancellationToken)
    {
        var config = await _settings.GetVoucherRenewalConfigAsync(cancellationToken);
        return Ok(RenewalConfigResponse.From(config));
    }

    /// <summary>
    /// Read-only term quote for one cart line: whether short-term selling is on, and every term with the
    /// discount and the price the customer would actually pay for that nominal. So the picker can show
    /// real numbers and grey out terms the manager has not configured.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>renew/quote</c>: optimistic and non-binding. Nothing is reserved and no price is frozen
    /// here - <c>POST api/purchases/bulk</c> recomputes everything server-side and refuses the line if it
    /// would sell below cost.
    /// </remarks>
    [HttpGet("term-quote")]
    [ProducesResponseType(typeof(TermQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTermQuote(
        [FromQuery] string stationId,
        [FromQuery] string fuelTypeId,
        [FromQuery] decimal liters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stationId) || string.IsNullOrWhiteSpace(fuelTypeId) || liters <= 0m)
            return BadRequest("stationId, fuelTypeId and a positive liters are required.");

        var quote = await _termQuoteHandler.HandleAsync(stationId, fuelTypeId, liters, cancellationToken);
        return Ok(quote);
    }

    /// <summary>
    /// Read-only price/eligibility preview for a batch of the caller's own vouchers: per voucher,
    /// whether it is renewable, its branch, and the eight tiers with price + availability — so the
    /// term picker can disable an unbuyable tier («тимчасово недоступно») before payment. Mutates
    /// nothing and never mints an invoice; the authoritative gate still runs at <c>renew</c>.
    /// </summary>
    [HttpPost("renew/quote")]
    [EnableRateLimiting(PurchasePolicy)]
    [ProducesResponseType(typeof(RenewalQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> QuoteRenewal([FromBody] RenewalQuoteCommand command, CancellationToken cancellationToken)
    {
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
            var response = await _renewalQuoteHandler.HandleAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (VoucherRenewalException ex)
        {
            return BadRequest(new { code = ex.Code, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error quoting renewal for user {UserId}", userId);
            return StatusCode(500, "An error occurred while quoting the renewal");
        }
    }

    /// <summary>
    /// Creates a paid renewal/replacement checkout for a batch of the caller's own vouchers and
    /// returns one Monobank invoice for the lot. No voucher changes until the payment webhook lands;
    /// business rejections (feature off, voucher not renewable, tier unavailable, no stock) answer
    /// 400 with a stable code, an inactive account answers 403.
    /// </summary>
    [HttpPost("renew")]
    [EnableRateLimiting(PurchasePolicy)]
    [ProducesResponseType(typeof(RenewalCheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RenewVouchers([FromBody] RenewalCheckoutCommand command, CancellationToken cancellationToken)
    {
        if (command.Items == null || command.Items.Count == 0)
            return BadRequest("At least one voucher is required");

        if (command.Items.Count > MaxBulkItems)
            return BadRequest($"A renewal batch may contain at most {MaxBulkItems} vouchers");

        foreach (var item in command.Items)
        {
            if (item.VoucherId == Guid.Empty)
                return BadRequest("VoucherId is required for every item");

            if (string.IsNullOrWhiteSpace(item.TermCode))
                return BadRequest("TermCode is required for every item");
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
            var response = await _renewalCheckoutHandler.HandleAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (VoucherRenewalException ex)
        {
            return BadRequest(new { code = ex.Code, message = ex.Message });
        }
        catch (AccountInactiveException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = AccountInactiveException.Code, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating renewal checkout for user {UserId}", userId);
            return StatusCode(500, "An error occurred while creating the renewal");
        }
    }

    [HttpPost]
    [EnableRateLimiting(PurchasePolicy)]
    [ProducesResponseType(typeof(CreateCheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
        catch (BelowCostSaleBlockedException ex)
        {
            return Conflict(new { code = BelowCostSaleBlockedException.Code, message = ex.Message });
        }
        catch (AccountInactiveException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = AccountInactiveException.Code, message = ex.Message });
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
    /// The signed-in customer's own savings summary: paid amount, litres bought, saving vs the pump
    /// (frozen at purchase), remaining unredeemed balance and a per-month breakdown. Optionally
    /// narrowed to a period via <paramref name="fromDate"/>/<paramref name="toDate"/> (order date);
    /// remaining balance is always a current snapshot. Never exposes cost, margin or loss.
    /// </summary>
    [HttpGet("savings")]
    [ProducesResponseType(typeof(SavingsReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySavings(
        CancellationToken cancellationToken,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
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
            var report = await _getSavingsReportHandler.HandleAsync(
                new GetSavingsReportQuery(Guid.Parse(userId), fromDate, toDate), cancellationToken);
            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving savings report for user {UserId}", userId);
            return StatusCode(500, "An error occurred while retrieving the savings report");
        }
    }

    /// <summary>
    /// Soft-deletes one of the caller's own checkouts that was never paid (PendingPayment).
    /// Any other state — or someone else's order — answers 404 so the endpoint doubles as
    /// nothing worth probing. A paid-but-unfulfilled invoice must stay visible: if the
    /// Monobank webhook lands after this delete the money is already taken, so the order
    /// has to remain for the refund/admin path to see it.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMyOrder(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value
                     ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return Unauthorized("User ID not found");
        }

        var deleted = await _deleteMyOrderHandler.HandleAsync(
            new DeleteMyOrderCommand(id, Guid.Parse(userId)), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Simulate payment for testing. Development only: this marks an order paid without any
    /// money moving, so in production it is a voucher-minting primitive for anyone holding an
    /// Admin token. Answers 404 outside Development so its existence is not confirmed either.
    /// </summary>
    [HttpPost("simulate")]
    [Authorize(Policy = "Staff")]
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