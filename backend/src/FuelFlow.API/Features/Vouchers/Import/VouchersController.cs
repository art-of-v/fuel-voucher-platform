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
    private readonly ImportConcurrencyGuard _importGuard;
    private readonly ILogger<VouchersController> _logger;

    public VouchersController(
        ImportVouchersCommandHandler importHandler,
        GetVouchersQueryHandler getHandler,
        IQrGenerator qrGenerator,
        ProviderEventService eventService,
        ImportConcurrencyGuard importGuard,
        ILogger<VouchersController> logger)
    {
        _importHandler = importHandler;
        _getHandler = getHandler;
        _qrGenerator = qrGenerator;
        _eventService = eventService;
        _importGuard = importGuard;
        _logger = logger;
    }

    [HttpPost("import")]
    [Authorize(Policy = "Staff")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25_000_000)]
    [ProducesResponseType(typeof(ImportVouchersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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

        // Rendering happens in-request, so concurrent imports multiply peak memory. Refuse the
        // second one rather than letting them stack.
        if (!_importGuard.TryAcquire())
        {
            _logger.LogWarning("Voucher import rejected by {Admin}: another import is already running", adminName);
            return Conflict("Another voucher import is already in progress. Wait for it to finish and retry.");
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
        finally
        {
            _importGuard.Release();
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
    [Authorize(Policy = "Staff")]
    [ProducesResponseType(typeof(GetVouchersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

        // Authorization must agree with MarkVoucherAsUsedCommandHandler, because both endpoints
        // hand out control of the same bearer instrument: the QR payload IS what a station scans.
        //
        // The previous check tested AssignedToUserId only. GiftVouchersCommand transfers a company
        // voucher by setting WorkerUserId and LEAVING AssignedToUserId on the owner, so that check
        // was wrong in both directions at once: the worker the voucher now belongs to got 403 on
        // their own QR, and the owner who gifted it away could still pull the redeemable QR and
        // spend it at the pump - while mark-used refused to let that same owner mark it Used, so
        // the worker's app went on showing Assigned for a voucher that was already burned.
        if (!User.IsInRole("Admin"))
        {
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Forbid();

            var holder = voucher.WorkerUserId ?? voucher.AssignedToUserId;
            if (holder != userId)
                return Forbid();
        }

        // Clamp: unbounded width/height let any authenticated caller ask for a
        // gigapixel PNG and OOM the container (no per-container memory limit).
        width = Math.Clamp(width, MinQrSize, MaxQrSize);
        height = Math.Clamp(height, MinQrSize, MaxQrSize);

        var base64 = _qrGenerator.GenerateQrCode(
            voucher.QrPayload, width, height,
            voucher.QrParameters?.EccLevel,
            voucher.QrParameters?.Version,
            voucher.QrParameters?.EncodingMode,
            voucher.QrParameters?.MaskPattern);
        var bytes = Convert.FromBase64String(base64);
        return File(bytes, "image/png");
    }

    private const int MinQrSize = 32;
    private const int MaxQrSize = 2000;
}
