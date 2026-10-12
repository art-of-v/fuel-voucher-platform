using FuelFlow.Features.Admin.GetReconciliation;
using FuelFlow.Features.Orders.GetAdminPurchases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "Staff")]
public sealed class AdminController : ControllerBase
{
    private readonly GetReconciliationQueryHandler _getReconciliationHandler;
    private readonly GetAdminPurchasesQueryHandler _getAdminPurchasesHandler;

    public AdminController(
        GetReconciliationQueryHandler getReconciliationHandler,
        GetAdminPurchasesQueryHandler getAdminPurchasesHandler)
    {
        _getReconciliationHandler = getReconciliationHandler;
        _getAdminPurchasesHandler = getAdminPurchasesHandler;
    }

    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(GetReconciliationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReconciliation(CancellationToken cancellationToken)
    {
        var result = await _getReconciliationHandler.HandleAsync(new GetReconciliationQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("purchases")]
    [ProducesResponseType(typeof(List<AdminPurchaseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPurchases(CancellationToken cancellationToken)
    {
        var result = await _getAdminPurchasesHandler.HandleAsync(new GetAdminPurchasesQuery(), cancellationToken);
        return Ok(result);
    }
}
