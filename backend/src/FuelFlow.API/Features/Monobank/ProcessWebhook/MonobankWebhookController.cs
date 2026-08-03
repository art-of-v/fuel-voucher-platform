using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
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
    private readonly IAsymmetricSignatureVerifier _signatureVerifier;
    private readonly MonobankOptions _options;
    private readonly ILogger<MonobankWebhookController> _logger;

    public MonobankWebhookController(
        ProcessMonobankWebhookCommandHandler handler,
        IAsymmetricSignatureVerifier signatureVerifier,
        IOptions<MonobankOptions> options,
        ILogger<MonobankWebhookController> logger)
    {
        _handler = handler;
        _signatureVerifier = signatureVerifier;
        _options = options.Value;
        _logger = logger;
    }

    /// <remarks>
    /// Monobank signs the raw request body with its private key; X-Sign carries the base64 signature.
    /// When Monobank is enabled we fail closed: missing or invalid signatures are rejected with 401.
    /// </remarks>
    [HttpPost("webhook")]
    public async Task<IActionResult> ProcessWebhook(CancellationToken cancellationToken)
    {
        try
        {
            Request.EnableBuffering();

            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync(cancellationToken);
            }

            Request.Body.Position = 0;

            var signature = Request.Headers["X-Sign"].FirstOrDefault();

            if (_options.Enabled)
            {
                var verification = VerifySignature(signature, rawBody);
                if (verification != null)
                {
                    return verification;
                }
            }
            else
            {
                _logger.LogWarning(
                    "Monobank webhook processed WITHOUT signature verification (Monobank:Enabled is false)");
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
                if (response.ErrorCode == "AMOUNT_MISMATCH")
                {
                    _logger.LogError(
                        "Monobank webhook rejected for invoice {InvoiceId}: {Message}",
                        command.InvoiceId, response.Message);

                    return BadRequest(response.Message);
                }

                _logger.LogWarning("Webhook processing failed: {Message}", response.Message);
                return NotFound(response.Message);
            }

            _logger.LogInformation(
                "Webhook processed: Order {OrderId} status {NewStatus} ({Message})",
                response.OrderId,
                response.NewStatus,
                response.Message);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Monobank webhook");
            return StatusCode(500, "Internal server error");
        }
    }

    private IActionResult? VerifySignature(string? signature, string rawBody)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("Monobank webhook rejected: missing X-Sign header");
            return Unauthorized(new { error = "Missing signature" });
        }

        if (string.IsNullOrWhiteSpace(_options.PublicKey))
        {
            _logger.LogError(
                "Monobank webhook cannot be verified: Monobank:PublicKey is not configured");
            return StatusCode(500, "Webhook verification not configured");
        }

        if (!_signatureVerifier.Verify(rawBody, signature, _options.PublicKey))
        {
            _logger.LogWarning(
                "Monobank webhook rejected: invalid signature (fingerprint {Fingerprint})",
                Fingerprint(signature));
            return Unauthorized(new { error = "Invalid signature" });
        }

        return null;
    }

    private static string Fingerprint(string signature)
    {
        if (signature.Length <= 16)
        {
            return "short";
        }

        return signature[..8] + "..." + signature[^8..];
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
