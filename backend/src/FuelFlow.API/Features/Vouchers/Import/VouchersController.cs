using FuelFlow.Features.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace FuelFlow.Features.Vouchers.Import;

[ApiController]
[Route("api/voucher-catalog")]
public sealed class VouchersController : ControllerBase
{
    private readonly ImportVouchersCommandHandler _importHandler;
    private readonly GetVouchersQueryHandler _getHandler;
    private readonly IQrGenerator _qrGenerator;
    private readonly ProviderEventService _eventService;
    private readonly ILogger<VouchersController> _logger;

    public VouchersController(
        ImportVouchersCommandHandler importHandler,
        GetVouchersQueryHandler getHandler,
        IQrGenerator qrGenerator,
        ProviderEventService eventService,
        ILogger<VouchersController> logger)
    {
        _importHandler = importHandler;
        _getHandler = getHandler;
        _qrGenerator = qrGenerator;
        _eventService = eventService;
        _logger = logger;
    }

    [HttpPost("import")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25_000_000)]
    [ProducesResponseType(typeof(ImportVouchersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ImportVouchers(IFormFile file, CancellationToken cancellationToken)
    {
        var adminName = User.FindFirst("first_name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";

        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Voucher import rejected by {Admin}: no file uploaded", adminName);
            return BadRequest("No file was uploaded.");
        }

        if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Voucher import rejected by {Admin}: file '{FileName}' ({Size} bytes) is not a PDF", adminName, file.FileName, file.Length);
            return BadRequest("Only PDF files are supported.");
        }

        if (!await HasPdfMagicBytesAsync(file, cancellationToken))
        {
            _logger.LogWarning("Voucher import rejected by {Admin}: file '{FileName}' ({Size} bytes) has no PDF magic bytes", adminName, file.FileName, file.Length);
            return BadRequest("Only PDF files are supported.");
        }

        try
        {
            using var stream = file.OpenReadStream();
            var command = new ImportVouchersCommand(stream, file.FileName);
            var result = await _importHandler.HandleAsync(command, cancellationToken);

            var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(adminIdClaim, out var adminId))
            {
                await _eventService.RecordEventAsync(
                    "Voucher",
                    result.ImportId.ToString(),
                    "VoucherImported",
                    null,
                    JsonSerializer.Serialize(new
                    {
                        fileName = file.FileName,
                        imported = result.Imported,
                        duplicates = result.Duplicates,
                        failed = result.Failed,
                        verificationFailed = result.VerificationFailed
                    }),
                    adminId,
                    adminName,
                    $"Imported {result.Imported} vouchers from {file.FileName}",
                    "all",
                    cancellationToken);
            }

            return Ok(result);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Voucher import rejected by {Admin}: {Message}", adminName, ex.Message);
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voucher import failed for file '{FileName}' ({Size} bytes) by {Admin}", file.FileName, file.Length, adminName);
            throw;
        }
    }

    private static async Task<bool> HasPdfMagicBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var probe = file.OpenReadStream();
        var header = new byte[4];
        var read = await probe.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        return read == header.Length
            && header[0] == (byte)'%'
            && header[1] == (byte)'P'
            && header[2] == (byte)'D'
            && header[3] == (byte)'F';
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
            if (userId == null || voucher.AssignedToUserId != Guid.Parse(userId))
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
