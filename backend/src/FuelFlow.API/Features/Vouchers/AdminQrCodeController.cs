using FuelFlow.Features.Vouchers.GetQrCodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/qr-codes")]
[Authorize(Roles = "Admin")]
public sealed class AdminQrCodeController : ControllerBase
{
    private readonly GetQrCodesQueryHandler _handler;

    public AdminQrCodeController(GetQrCodesQueryHandler handler)
    {
        _handler = handler;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new GetQrCodesQuery(), cancellationToken);
        return Ok(result);
    }
}
