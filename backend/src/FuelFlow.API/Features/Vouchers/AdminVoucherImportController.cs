using System.Security.Claims;
using FuelFlow.Features.Vouchers.GetImportBatches;
using FuelFlow.Features.Vouchers.ParseInvoice;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/voucher-imports")]
[Authorize(Policy = "Staff")]
public sealed class AdminVoucherImportController : ControllerBase
{
    private readonly GetImportBatchesQueryHandler _getAllHandler;
    private readonly GetImportBatchByIdQueryHandler _getByIdHandler;
    private readonly GetImportBatchVouchersQueryHandler _getVouchersHandler;
    private readonly GetImportBatchCostsQueryHandler _getBatchCostsHandler;
    private readonly GetImportBatchPnlQueryHandler _getBatchPnlHandler;
    private readonly SetBatchCostCommandHandler _setBatchCostHandler;
    private readonly ParseInvoiceCommandHandler _parseInvoiceHandler;

    public AdminVoucherImportController(
        GetImportBatchesQueryHandler getAllHandler,
        GetImportBatchByIdQueryHandler getByIdHandler,
        GetImportBatchVouchersQueryHandler getVouchersHandler,
        GetImportBatchCostsQueryHandler getBatchCostsHandler,
        GetImportBatchPnlQueryHandler getBatchPnlHandler,
        SetBatchCostCommandHandler setBatchCostHandler,
        ParseInvoiceCommandHandler parseInvoiceHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _getVouchersHandler = getVouchersHandler;
        _getBatchCostsHandler = getBatchCostsHandler;
        _getBatchPnlHandler = getBatchPnlHandler;
        _setBatchCostHandler = setBatchCostHandler;
        _parseInvoiceHandler = parseInvoiceHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _getAllHandler.HandleAsync(new GetImportBatchesQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getByIdHandler.HandleAsync(new GetImportBatchByIdQuery(id), cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("{id:guid}/vouchers")]
    public async Task<IActionResult> GetVouchers(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getVouchersHandler.HandleAsync(new GetImportBatchVouchersQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/batch-costs")]
    public async Task<IActionResult> GetBatchCosts(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getBatchCostsHandler.HandleAsync(new GetImportBatchCostsQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/batch-pnl")]
    public async Task<IActionResult> GetBatchPnl(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getBatchPnlHandler.HandleAsync(new GetImportBatchPnlQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id:guid}/batch-costs/{fuelTypeId}")]
    public async Task<IActionResult> SetBatchCost(
        Guid id,
        [FromRoute] string fuelTypeId,
        [FromBody] SetBatchCostRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _setBatchCostHandler.HandleAsync(
            new SetBatchCostCommand(id, fuelTypeId, request.CostPerLiter, GetUserId(), GetUserName()),
            cancellationToken);

        if (result.NotFound) return NotFound(new { error = result.Error });
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { success = true, blendedCostPerLiter = result.BlendedCostPerLiter, packagesRepriced = result.PackagesRepriced });
    }

    /// <summary>
    /// Parse an uploaded supplier invoice (накладна, .xlsx) and preview a suggested cost/liter per
    /// fuel, matched against this import's batches (pricing epic RQ-3). Read-only — the operator
    /// reviews the result and commits each line through <see cref="SetBatchCost"/>.
    /// </summary>
    [HttpPost("{id:guid}/parse-invoice")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> ParseInvoice(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "A non-empty .xlsx file is required" });

        InvoiceParseResult parsed;
        // ClosedXML needs a seekable stream; buffer the (≤20 MB) upload into memory first.
        using (var buffer = new MemoryStream())
        {
            await using (var upload = file.OpenReadStream())
                await upload.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            parsed = InvoiceImportParser.Parse(buffer);
        }

        var result = await _parseInvoiceHandler.HandleAsync(
            new ParseInvoiceCommand(id, parsed.Lines, parsed.Errors), cancellationToken);

        if (result.NotFound) return NotFound(new { error = "Import not found" });
        return Ok(result.Dto);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (first is not null && last is not null) return $"{first} {last}";
        if (first is not null) return first;
        if (last is not null) return last;
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}
