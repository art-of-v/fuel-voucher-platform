using System.Security.Claims;
using FuelFlow.Features.Vouchers.GetImportBatches;
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

    public AdminVoucherImportController(
        GetImportBatchesQueryHandler getAllHandler,
        GetImportBatchByIdQueryHandler getByIdHandler,
        GetImportBatchVouchersQueryHandler getVouchersHandler,
        GetImportBatchCostsQueryHandler getBatchCostsHandler,
        GetImportBatchPnlQueryHandler getBatchPnlHandler,
        SetBatchCostCommandHandler setBatchCostHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _getVouchersHandler = getVouchersHandler;
        _getBatchCostsHandler = getBatchCostsHandler;
        _getBatchPnlHandler = getBatchPnlHandler;
        _setBatchCostHandler = setBatchCostHandler;
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
