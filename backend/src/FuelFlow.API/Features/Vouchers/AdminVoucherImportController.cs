using System.Security.Claims;
using FuelFlow.Features.Vouchers.GetImportBatches;
using FuelFlow.Features.Vouchers.ParseInvoice;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

/// <summary>What the admin asked the batch to do.</summary>
public sealed record ImportBatchBulkActionRequest(
    string Action,
    string? ActingAdminName = null);

[ApiController]
[Route("api/admin/voucher-imports")]
public sealed class AdminVoucherImportController : ControllerBase
{
    private readonly GetImportBatchesQueryHandler _getAllHandler;
    private readonly GetImportBatchByIdQueryHandler _getByIdHandler;
    private readonly GetImportBatchVouchersQueryHandler _getVouchersHandler;
    private readonly GetImportBatchCostsQueryHandler _getBatchCostsHandler;
    private readonly GetImportBatchPnlQueryHandler _getBatchPnlHandler;
    private readonly GetImportBatchCostTimelineQueryHandler _getCostTimelineHandler;
    private readonly SetBatchCostCommandHandler _setBatchCostHandler;
    private readonly ParseInvoiceCommandHandler _parseInvoiceHandler;
    private readonly ImportBatchBulkActionQueryHandler _bulkActionHandler;

    public AdminVoucherImportController(
        GetImportBatchesQueryHandler getAllHandler,
        GetImportBatchByIdQueryHandler getByIdHandler,
        GetImportBatchVouchersQueryHandler getVouchersHandler,
        GetImportBatchCostsQueryHandler getBatchCostsHandler,
        GetImportBatchPnlQueryHandler getBatchPnlHandler,
        GetImportBatchCostTimelineQueryHandler getCostTimelineHandler,
        SetBatchCostCommandHandler setBatchCostHandler,
        ParseInvoiceCommandHandler parseInvoiceHandler,
        ImportBatchBulkActionQueryHandler bulkActionHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _getVouchersHandler = getVouchersHandler;
        _getBatchCostsHandler = getBatchCostsHandler;
        _getBatchPnlHandler = getBatchPnlHandler;
        _getCostTimelineHandler = getCostTimelineHandler;
        _setBatchCostHandler = setBatchCostHandler;
        _parseInvoiceHandler = parseInvoiceHandler;
        _bulkActionHandler = bulkActionHandler;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _getAllHandler.HandleAsync(new GetImportBatchesQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getByIdHandler.HandleAsync(new GetImportBatchByIdQuery(id), cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// The batch's vouchers, a page at a time, with the count of the whole batch.
    /// </summary>
    [HttpGet("{id:guid}/vouchers")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetVouchers(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _getVouchersHandler.HandleAsync(
            new GetImportBatchVouchersQuery(id, page, pageSize),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// The whole batch, activated or deactivated at once.
    /// </summary>
    /// <remarks>
    /// The two buttons in the admin used to act on the ids the tab had loaded, which was the whole
    /// batch only because the list was unbounded. Now that the list is a page, the action is the
    /// server's: it names the import, and the database decides which rows that is.
    /// </remarks>
    [HttpPost("{id:guid}/bulk-action")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    public async Task<IActionResult> BulkAction(
        Guid id,
        [FromBody] ImportBatchBulkActionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _bulkActionHandler.HandleAsync(
            new ImportBatchBulkActionQuery(
                id,
                request.Action,
                GetUserId(),
                request.ActingAdminName),
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result);
    }

    [HttpGet("{id:guid}/batch-costs")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetBatchCosts(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getBatchCostsHandler.HandleAsync(new GetImportBatchCostsQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/batch-pnl")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetBatchPnl(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getBatchPnlHandler.HandleAsync(new GetImportBatchPnlQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// How this batch's cost got to where it is: every exchange that took vouchers out of it, what
    /// that cost, and where the batch stands now (#178).
    /// </summary>
    [HttpGet("{id:guid}/cost-timeline")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetCostTimeline(Guid id, CancellationToken cancellationToken)
    {
        var result = await _getCostTimelineHandler.HandleAsync(
            new GetImportBatchCostTimelineQuery(id),
            cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id:guid}/batch-costs/{fuelTypeId}")]
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
    public async Task<IActionResult> SetBatchCost(
        Guid id,
        [FromRoute] Guid fuelTypeId,
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
    [Authorize(Policy = AuthPolicies.VoucherWrite)]
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
