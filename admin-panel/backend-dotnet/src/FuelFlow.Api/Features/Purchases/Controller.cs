using Microsoft.AspNetCore.Mvc;
using FuelFlow.Api.Infrastructure.Services;
using FuelFlow.Api.Features.QrCodes;
using FuelFlow.Api.Features.FuelPackages;

namespace FuelFlow.Api.Features.Purchases;

[ApiController]
[Route("api/admin/purchases")]
public class PurchasesController : ControllerBase
{
    private readonly IPurchaseRepository _repository;

    public PurchasesController(IPurchaseRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var purchases = await _repository.GetAllAsync();
        return Ok(purchases);
    }
}

[ApiController]
[Route("api/purchases")]
public class PublicPurchasesController : ControllerBase
{
    private readonly IPurchaseRepository _purchaseRepo;
    private readonly IFuelPackageRepository _packageRepo;
    private readonly IStripeService _stripeService;

    public PublicPurchasesController(
        IPurchaseRepository purchaseRepo,
        IFuelPackageRepository packageRepo,
        IStripeService stripeService)
    {
        _purchaseRepo = purchaseRepo;
        _packageRepo = packageRepo;
        _stripeService = stripeService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyPurchases()
    {
        var sessionId = HttpContext.Session.Id;
        var purchases = await _purchaseRepo.GetBySessionIdAsync(sessionId);
        return Ok(purchases);
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyPurchasesAlt()
    {
        var userId = HttpContext.Session.GetString("userId") ?? "dev-user-123";
        var purchases = await _purchaseRepo.GetByUserIdAsync(userId);
        
        var enrichedPurchases = new List<object>();
        foreach (var purchase in purchases)
        {
            if ((purchase.QrCodeId.HasValue || purchase.VoucherId.HasValue) && purchase.Status == "delivered")
            {
                var withQr = await _purchaseRepo.GetByIdWithQrCodeAsync(purchase.Id);
                enrichedPurchases.Add(withQr ?? (object)purchase);
            }
            else
            {
                enrichedPurchases.Add(purchase);
            }
        }

        return Ok(enrichedPurchases);
    }

    [HttpGet("session/{sessionId}")]
    public async Task<IActionResult> GetBySession(string sessionId)
    {
        var purchases = await _purchaseRepo.GetByUserIdAsync(sessionId);
        
        var enrichedPurchases = new List<object>();
        foreach (var purchase in purchases)
        {
            if ((purchase.QrCodeId.HasValue || purchase.VoucherId.HasValue) && purchase.Status == "delivered")
            {
                var withQr = await _purchaseRepo.GetByIdWithQrCodeAsync(purchase.Id);
                enrichedPurchases.Add(withQr ?? (object)purchase);
            }
            else
            {
                enrichedPurchases.Add(purchase);
            }
        }

        return Ok(enrichedPurchases);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseRequest request)
    {
        var sessionId = HttpContext.Session.Id;
        var requestWithSession = request with { SessionId = sessionId };
        
        var purchase = await _purchaseRepo.CreateAsync(requestWithSession);
        return Ok(purchase);
    }

    [HttpPost("{id}/checkout")]
    public async Task<IActionResult> CreateCheckout(int id)
    {
        var purchase = await _purchaseRepo.GetByIdAsync(id);
        if (purchase == null)
            return NotFound(new { message = "Purchase not found" });

        var session = await _stripeService.CreateCheckoutSessionAsync(
            purchase.PackageId,
            purchase.StationName,
            purchase.FuelName,
            purchase.Liters,
            purchase.Price
        );

        await _purchaseRepo.SetStripeSessionIdAsync(id, session.Id);

        return Ok(new { sessionId = session.Id, url = session.Url });
    }

    [HttpGet("config")]
    public IActionResult GetStripeConfig()
    {
        return Ok(new { publishableKey = _stripeService.GetPublishableKey() });
    }
}
