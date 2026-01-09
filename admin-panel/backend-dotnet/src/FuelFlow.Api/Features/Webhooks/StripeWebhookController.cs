using Microsoft.AspNetCore.Mvc;
using FuelFlow.Api.Features.Purchases;
using FuelFlow.Api.Features.QrCodes;
using FuelFlow.Api.Features.Vouchers;

namespace FuelFlow.Api.Features.Webhooks;

[ApiController]
[Route("api/stripe/webhook")]
public class StripeWebhookController : ControllerBase
{
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IQrCodeRepository _qrCodeRepo;
    private readonly IVoucherRepository _voucherRepo;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        IPurchaseRepository purchaseRepo,
        IQrCodeRepository qrCodeRepo,
        IVoucherRepository voucherRepo,
        ILogger<StripeWebhookController> logger)
    {
        _purchaseRepo = purchaseRepo;
        _qrCodeRepo = qrCodeRepo;
        _voucherRepo = voucherRepo;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook()
    {
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
            return BadRequest(new { error = "Missing signature" });

        try
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync();

            var webhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
            if (string.IsNullOrEmpty(webhookSecret))
            {
                _logger.LogError("STRIPE_WEBHOOK_SECRET not configured");
                return StatusCode(500, new { error = "Webhook not configured" });
            }

            var stripeEvent = Stripe.EventUtility.ConstructEvent(json, signature, webhookSecret);

            if (stripeEvent.Type == "checkout.session.completed")
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                if (session != null)
                {
                    await HandleCheckoutCompleteAsync(session);
                }
            }

            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook error");
            return BadRequest(new { error = "Webhook processing error" });
        }
    }

    [HttpPost("{uuid}")]
    public async Task<IActionResult> HandleWebhookWithUuid(string uuid)
    {
        return await HandleWebhook();
    }

    private async Task HandleCheckoutCompleteAsync(Stripe.Checkout.Session session)
    {
        var purchase = await _purchaseRepo.GetByStripeSessionIdAsync(session.Id);
        if (purchase == null)
        {
            _logger.LogWarning($"Purchase not found for Stripe session {session.Id}");
            return;
        }

        var voucher = await _voucherRepo.FindAvailableAsync(
            purchase.StationId,
            purchase.FuelType,
            purchase.Liters
        );

        if (voucher != null)
        {
            await _purchaseRepo.AssignVoucherAsync(purchase.Id, voucher.Id);
            await _voucherRepo.MarkAsUsedAsync(voucher.Id);
            _logger.LogInformation($"Assigned voucher {voucher.Id} to purchase {purchase.Id}");
            return;
        }

        var qrCode = await _qrCodeRepo.FindAvailableAsync(
            purchase.StationId,
            purchase.FuelType,
            purchase.Liters
        );

        if (qrCode != null)
        {
            await _purchaseRepo.AssignQrCodeAsync(purchase.Id, qrCode.Id);
            await _qrCodeRepo.MarkAsSoldAsync(qrCode.Id, purchase.Id);
            _logger.LogInformation($"Assigned QR code {qrCode.Id} to purchase {purchase.Id}");
        }
        else
        {
            await _purchaseRepo.UpdateStatusAsync(purchase.Id, "pending_qr");
            _logger.LogWarning($"No QR code or voucher available for purchase {purchase.Id}");
        }
    }
}
