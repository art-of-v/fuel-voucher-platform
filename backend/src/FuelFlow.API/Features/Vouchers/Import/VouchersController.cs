using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FuelFlow.Features.Vouchers.Import;

[ApiController]
[Route("api/[controller]")]
public sealed class VouchersController : ControllerBase
{
    private readonly ImportVouchersCommandHandler _importHandler;
    private readonly GetVouchersQueryHandler _getHandler;
    private readonly IQrGenerator _qrGenerator;

    public VouchersController(ImportVouchersCommandHandler importHandler, GetVouchersQueryHandler getHandler, IQrGenerator qrGenerator)
    {
        _importHandler = importHandler;
        _getHandler = getHandler;
        _qrGenerator = qrGenerator;
    }

    [HttpPost("import")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ImportVouchersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ImportVouchers(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file was uploaded.");

        if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only PDF files are supported.");

        using var stream = file.OpenReadStream();
        var command = new ImportVouchersCommand(stream, file.FileName);
        var result = await _importHandler.HandleAsync(command, cancellationToken);

        return Ok(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetVouchersResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVouchers([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var query = new GetVouchersQuery(page, pageSize);
        var result = await _getHandler.HandleAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/qr")]
    [Authorize]
    [Produces("image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVoucherQr(Guid id, [FromQuery] int width = 300, [FromQuery] int height = 300, CancellationToken cancellationToken = default)
    {
        var voucher = await _getHandler.GetVoucherByIdAsync(id, cancellationToken);
        if (voucher == null)
            return NotFound();

        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (voucher.AssignedToUserId != userId)
                return Forbid();
        }

        var base64 = _qrGenerator.GenerateQrCode(
            voucher.QrPayload, width, height,
            voucher.QrParameters?.EccLevel,
            voucher.QrParameters?.Version,
            voucher.QrParameters?.EncodingMode,
            voucher.QrParameters?.MaskPattern);
        var bytes = Convert.FromBase64String(base64);
        return File(bytes, "image/png");
    }
}
