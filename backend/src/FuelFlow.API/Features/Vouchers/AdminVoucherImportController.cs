using FuelFlow.Features.Vouchers.GetImportBatches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/voucher-imports")]
[Authorize(Roles = "Admin")]
public sealed class AdminVoucherImportController : ControllerBase
{
    private readonly GetImportBatchesQueryHandler _getAllHandler;
    private readonly GetImportBatchByIdQueryHandler _getByIdHandler;
    private readonly GetImportBatchVouchersQueryHandler _getVouchersHandler;

    public AdminVoucherImportController(
        GetImportBatchesQueryHandler getAllHandler,
        GetImportBatchByIdQueryHandler getByIdHandler,
        GetImportBatchVouchersQueryHandler getVouchersHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _getVouchersHandler = getVouchersHandler;
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
}
