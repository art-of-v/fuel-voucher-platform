using FuelFlow.Features.Vouchers.GetQrCodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/qr-codes")]
[Authorize(Policy = "Staff")]
public sealed class AdminQrCodeController : ControllerBase
{
    private readonly GetQrCodesQueryHandler _handler;

    public AdminQrCodeController(GetQrCodesQueryHandler handler)
    {
        _handler = handler;
    }

    // Disabled 2026-10-12 (planning #289, delete by 2026-11-12): no caller in any client, script, deploy config, doc or test.
    // [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new GetQrCodesQuery(), cancellationToken);
        return Ok(result);
    }
}
