using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using static FuelFlow.API.Extensions.RateLimiterSetup;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

[ApiController]
[Route("api/monobank")]
[SystemScope]
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
    /// Missing or invalid signatures are rejected with 401, and a host with payments off refuses
    /// the delivery outright (503) rather than processing an unsigned body.
    /// <para>
    /// [AllowAnonymous] is explicit, not incidental: the caller is Monobank, which authenticates by
    /// signature rather than by bearer token, and AuthSetup installs a RequireAuthenticatedUser
    /// FallbackPolicy that would otherwise turn every payment callback into a silent 401 - money
    /// taken from the customer with no order fulfilled.
    /// </para>
    /// </remarks>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [EnableRateLimiting(MonobankWebhookPolicy)]
    public async Task<IActionResult> ProcessWebhook(CancellationToken cancellationToken)
    {
        // A host with payments off has no acquirer that could have signed a delivery, so an
        // unsigned body here can only be someone else's. Processing it - which this used to do,
        // with a warning - lets any caller name an invoice and declare it paid. Production cannot
        // enter this state without Monobank__AcknowledgeDisabledInProduction (see
        // SecurityConfigurationValidator), and even then the endpoint stays closed.
        if (!_options.Enabled)
        {
            _logger.LogError(
                "Monobank webhook refused: Monobank:Enabled is false on this host, so no delivery can be verified");
            return ApiError.ServiceUnavailable("Monobank payments are not enabled on this host.");
        }

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
            var keyId = Request.Headers["X-Key-Id"].FirstOrDefault();

            var verification = VerifySignature(signature, keyId, rawBody);
            if (verification != null)
            {
                return verification;
            }

            var webhookData = JsonSerializer.Deserialize<MonobankWebhookPayload>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (webhookData == null)
            {
                _logger.LogError("Failed to deserialize Monobank webhook payload");
                return ApiError.BadRequest("Invalid payload");
            }

            var command = new ProcessMonobankWebhookCommand
            {
                InvoiceId = webhookData.InvoiceId,
                Status = webhookData.Status,
                Amount = webhookData.Amount,
                CreatedDate = webhookData.CreatedDate,
                ModifiedDate = webhookData.ModifiedDate,
                CancelList = webhookData.CancelList,
                Signature = signature,
                KeyId = keyId,
                RawBody = rawBody
            };

            var response = await _handler.HandleAsync(command, cancellationToken);

            if (!response.Success)
            {
                if (response.ErrorCode == "AMOUNT_MISMATCH")
                {
                    // 200, not 400. A non-2xx tells Monobank to deliver again, and an amount
                    // mismatch is permanent - our price is not going to change because a callback
                    // arrives twice - so answering 400 turns one unresolvable event into an endless
                    // retry loop against our door, paid for in traffic.
                    //
                    // The money really was taken and the order stays PendingPayment, so nothing is
                    // fulfilled and no customer sees a paid order that was not. What is left is the
                    // evidence, and that is recorded: the delivery is on file as webhook_events with
                    // status 'Failed' and the acquirer's amount beside ours, which is what a dispute
                    // about this charge will be settled from.
                    _logger.LogError(
                        "Monobank webhook for invoice {InvoiceId} accepted without acting: {Message}",
                        command.InvoiceId, response.Message);

                    return Ok(response);
                }

                // A 404 here is a genuine race: the invoice exists at the acquirer while its order is
                // still committing. Retrying is the correct answer, and Monobank does it.
                _logger.LogWarning("Webhook processing failed: {Message}", response.Message);
                return ApiError.NotFound(response.Message);
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
            return ApiError.Result(StatusCodes.Status500InternalServerError, ApiError.UnexpectedError);
        }
    }

    private IActionResult? VerifySignature(string? signature, string? keyId, string rawBody)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            _logger.LogWarning("Monobank webhook rejected: missing X-Sign header");
            return ApiError.Unauthorized("Missing signature");
        }

        // One key for both merchants. Monobank signs webhooks with a key that belongs to the
        // COMPANY, not to a token or merchant profile: api.monobank.ua issues sandbox tokens for the
        // same company as the live cabinet, and both tokens return byte-identical keys from
        // /api/merchant/pubkey. Verified against production on 2026-10-11, where a sandbox payment
        // passed the live key on the first try. So a per-merchant key list would be a second copy of
        // the same value that can only ever disagree with the first by being wrong.
        //
        // Merchant routing never came from the key anyway - an invoice id resolves to an order, and
        // the order carries the merchant it was created against.
        var publicKey = ResolveLivePublicKey(keyId);
        if (!string.IsNullOrWhiteSpace(publicKey) && _signatureVerifier.Verify(rawBody, signature, publicKey))
        {
            return null;
        }

        _logger.LogWarning(
            "Monobank webhook rejected: invalid signature (fingerprint {Fingerprint}, key id {KeyId})",
            Fingerprint(signature),
            string.IsNullOrWhiteSpace(keyId) ? "(default)" : keyId);
        return ApiError.Unauthorized("Invalid signature");
    }

    private string? ResolveLivePublicKey(string? keyId)
    {
        if (!string.IsNullOrWhiteSpace(keyId))
        {
            if (_options.PublicKeys.TryGetValue(keyId, out var rotated))
            {
                return rotated;
            }

            _logger.LogWarning(
                "Monobank webhook referenced unknown key id '{KeyId}', falling back to default key", keyId);
        }

        return _options.PublicKey;
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
    public List<MonobankCancelListItem>? CancelList { get; set; }
}
