using Microsoft.AspNetCore.Mvc;
using FuelFlow.Api.Features.Purchases;
using FuelFlow.Api.Features.QrCodes;
using FuelFlow.Api.Features.Vouchers;
using FuelFlow.Api.Features.FuelPackages;
using FuelFlow.Api.Infrastructure.Services;

namespace FuelFlow.Api.Features.Checkout;

[ApiController]
[Route("api")]
public class CheckoutController : ControllerBase
{
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IQrCodeRepository _qrCodeRepo;
    private readonly IVoucherRepository _voucherRepo;
    private readonly IFuelPackageRepository _packageRepo;

    public CheckoutController(
        IPurchaseRepository purchaseRepo,
        IQrCodeRepository qrCodeRepo,
        IVoucherRepository voucherRepo,
        IFuelPackageRepository packageRepo)
    {
        _purchaseRepo = purchaseRepo;
        _qrCodeRepo = qrCodeRepo;
        _voucherRepo = voucherRepo;
        _packageRepo = packageRepo;
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request)
    {
        var userId = HttpContext.Session.GetString("userId") ?? "dev-user-123";

        var purchase = await _purchaseRepo.CreateAsync(new CreatePurchaseRequest
        {
            SessionId = userId,
            PackageId = request.PackageId,
            StationId = request.StationId,
            StationName = request.StationName,
            FuelType = request.FuelType,
            FuelName = request.FuelName,
            Liters = request.Liters,
            Price = request.Price
        });

        return Ok(new { purchaseId = purchase.Id });
    }

    [HttpPost("purchases/{id}/complete")]
    public async Task<IActionResult> CompletePurchase(int id)
    {
        var purchase = await _purchaseRepo.GetByIdAsync(id);
        if (purchase == null)
            return NotFound(new { error = "Purchase not found" });

        try
        {
            var voucher = await _voucherRepo.FindAvailableAsync(
                purchase.StationName,
                purchase.FuelName,
                purchase.Liters
            );

            if (voucher != null)
            {
                await _purchaseRepo.AssignVoucherAsync(id, voucher.Id);
                await _voucherRepo.MarkAsUsedAsync(voucher.Id);
            }
            else
            {
                var qrCode = await _qrCodeRepo.FindAvailableAsync(
                    purchase.StationId,
                    purchase.FuelName,
                    purchase.Liters
                );

                if (qrCode == null)
                    return NotFound(new { error = "No vouchers or QR codes available for this order." });

                await _qrCodeRepo.MarkAsSoldAsync(qrCode.Id, id);
                await _purchaseRepo.AssignQrCodeAsync(id, qrCode.Id);
            }

            var finalizedPurchase = await _purchaseRepo.GetByIdWithQrCodeAsync(id);
            return Ok(finalizedPurchase);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to complete purchase", details = ex.Message });
        }
    }

    [HttpPost("payments/simulate")]
    public async Task<IActionResult> SimulatePayment([FromBody] SimulatePaymentRequest request)
    {
        var purchase = await _purchaseRepo.GetByIdAsync(request.PurchaseId);
        if (purchase == null)
            return NotFound(new { error = "Purchase not found" });

        if (request.Scenario == "failure")
        {
            await _purchaseRepo.UpdateStatusAsync(request.PurchaseId, "failed");
            return Ok(new { status = "failed" });
        }

        try
        {
            var voucher = await _voucherRepo.FindAvailableAsync(
                purchase.StationName,
                purchase.FuelName,
                purchase.Liters
            );

            if (voucher != null)
            {
                await _purchaseRepo.AssignVoucherAsync(request.PurchaseId, voucher.Id);
                await _voucherRepo.MarkAsUsedAsync(voucher.Id);
            }
            else
            {
                await _purchaseRepo.UpdateStatusAsync(request.PurchaseId, "failed");
                return StatusCode(409, new { error = "No vouchers available", code = "BUSINESS_VIOLATION" });
            }

            var finalized = await _purchaseRepo.GetByIdWithQrCodeAsync(request.PurchaseId);
            return Ok(new { status = "success", purchase = finalized });
        }
        catch (Exception ex)
        {
            await _purchaseRepo.UpdateStatusAsync(request.PurchaseId, "failed");
            return StatusCode(409, new { error = ex.Message, code = "BUSINESS_VIOLATION" });
        }
    }

    [HttpGet("packages/station/{stationId}")]
    public async Task<IActionResult> GetPackagesByStation(string stationId)
    {
        var packages = await _packageRepo.GetByStationAsync(stationId);
        return Ok(packages);
    }

    [HttpGet("stripe/config")]
    public IActionResult GetStripeConfig([FromServices] IStripeService stripeService)
    {
        return Ok(new { publishableKey = stripeService.GetPublishableKey() });
    }
}

public record CheckoutRequest
{
    public string PackageId { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public string StationName { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
    public string FuelName { get; init; } = string.Empty;
    public int Liters { get; init; }
    public int Price { get; init; }
}

public record SimulatePaymentRequest
{
    public int PurchaseId { get; init; }
    public string Scenario { get; init; } = "success";
}
