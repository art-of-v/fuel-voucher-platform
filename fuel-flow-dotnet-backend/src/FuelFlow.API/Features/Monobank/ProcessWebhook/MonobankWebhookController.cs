using FuelFlow.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

[ApiController]
[Route("api/monobank")]
public sealed class MonobankWebhookController : ControllerBase
{
    private readonly ProcessMonobankWebhookCommandHandler _handler;
    private readonly MonobankOptions _options;
    private readonly ILogger<MonobankWebhookController> _logger;

    public MonobankWebhookController(
        ProcessMonobankWebhookCommandHandler handler,
        IOptions<MonobankOptions> options,
        ILogger<MonobankWebhookController> logger)
    {
        _handler = handler;
        _options = options.Value;
        _logger = logger;
    }

    /// <remarks>
    /// Monobank sends POST requests with X-Sign header for signature verification.
    /// Signature verification with public key should be implemented for production.
    /// </remarks>
    [HttpPost("webhook")]
    public async Task<IActionResult> ProcessWebhook(CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);

            _logger.LogInformation("Received Monobank webhook: {Body}", rawBody);

            var signature = Request.Headers["X-Sign"].FirstOrDefault();

            // TODO: Verify signature using Monobank public key
            if (!string.IsNullOrEmpty(signature))
            {
                _logger.LogInformation("Webhook signature present: {Signature}", signature);
            }
            else
            {
                _logger.LogWarning("Webhook signature missing (X-Sign header)");
            }

            var webhookData = JsonSerializer.Deserialize<MonobankWebhookPayload>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (webhookData == null)
            {
                _logger.LogError("Failed to deserialize Monobank webhook payload");
                return BadRequest("Invalid payload");
            }

            var command = new ProcessMonobankWebhookCommand
            {
                InvoiceId = webhookData.InvoiceId,
                Status = webhookData.Status,
                Amount = webhookData.Amount,
                CreatedDate = webhookData.CreatedDate,
                ModifiedDate = webhookData.ModifiedDate,
                Signature = signature,
                RawBody = rawBody
            };

            var response = await _handler.HandleAsync(command, cancellationToken);

            if (!response.Success)
            {
                _logger.LogWarning("Webhook processing failed: {Message}", response.Message);
                return NotFound(response.Message);
            }

            _logger.LogInformation(
                "Webhook processed successfully: Order {OrderId} status {NewStatus}",
                response.OrderId,
                response.NewStatus);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Monobank webhook");
            return StatusCode(500, "Internal server error");
        }
    }
}

internal sealed class MonobankWebhookPayload
{
    public string InvoiceId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long Amount { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
}
