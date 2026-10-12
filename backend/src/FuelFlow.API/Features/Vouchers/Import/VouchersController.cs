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
    private readonly ProviderEventService _eventService;
    private readonly ImportConcurrencyGuard _importGuard;
    private readonly ILogger<VouchersController> _logger;

    public VouchersController(
        ImportVouchersCommandHandler importHandler,
        ProviderEventService eventService,
        ImportConcurrencyGuard importGuard,
        ILogger<VouchersController> logger)
    {
        _importHandler = importHandler;
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
            var command = new ImportVouchersCommand(stream, file.FileName, supplierId);
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
}
