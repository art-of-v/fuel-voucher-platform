using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.SharedModels;
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
    private readonly AuditEventService _eventService;
    private readonly ImportConcurrencyGuard _importGuard;
    private readonly ILogger<VouchersController> _logger;

    public VouchersController(
        ImportVouchersCommandHandler importHandler,
        GetVouchersQueryHandler getHandler,
        IQrGenerator qrGenerator,
        AuditEventService eventService,
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
    public async Task<IActionResult> ImportVouchers([FromForm] IFormFile file, [FromForm] Guid supplierId, CancellationToken cancellationToken)
    {
        var adminName = User.FindFirst("first_name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
        // Resolved up front, not after the import: the ledger records who took delivery of this paper,
        // and `voucher_imports` still has no operator column to fall back on.
        _ = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId);

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
            var command = new ImportVouchersCommand(stream, file.FileName, supplierId, adminId);
            var result = await _importHandler.HandleAsync(command, cancellationToken);

            // adminId was resolved at the top of the method, for the import itself.
            if (adminId != Guid.Empty)
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
    /// <summary>
    /// Renders a voucher's QR as a PNG, for the holder or an Admin.
    ///
    /// <para>
    /// Kept although no current client calls it, and that is deliberate. The app is published in
    /// the App Store and AppVersion:MinimumVersion sits at the version the stores serve (1.1.0 since
    /// 2026-10-16), so the build in users' hands is exactly the one allowed through - and Expo Updates
    /// only reach a user the next time they launch, so a build from before the renderer moved to
    /// voucher.imageUrl can still be asking for this. It is the one endpoint whose absence takes a
    /// customer from "my fuel voucher" to "I cannot refuel": if a stale bundle calls it and gets a
    /// 404, the QR simply is not there at the pump. That asymmetry is why "no client in the repo
    /// calls it" was not enough reason to delete it here.
    /// </para>
    /// </summary>
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
        // hand out control of the same bearer instrument: the QR payload IS what a Brand scans.
        //
        // The previous check tested AssignedToUserId only. Issuing fuel to a worker sets
        // WorkerUserId and LEAVES AssignedToUserId on the owner, so that check was wrong in both
        // directions at once: the worker the voucher now belongs to got 403 on their own QR, and
        // the owner who issued it could still pull the redeemable QR and spend it at the pump -
        // while mark-used refused to let that same owner mark it Used, so the worker's app went on
        // showing Assigned for a voucher that was already burned.
        //
        // The rule itself now lives in VoucherHolder, shared with the wallet list: the two
        // endpoints disagreed once, and the list was the one that leaked.
        if (!User.IsInRole("Admin"))
        {
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Forbid();

            if (!VoucherHolder.CanRedeem(voucher.WorkerUserId, voucher.AssignedToUserId, userId))
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
