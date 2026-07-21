using FuelFlow.Features.Admin.GetDashboard;
using FuelFlow.Features.Admin.GetReconciliation;
using FuelFlow.Features.Orders.GetAdminPurchases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public sealed class AdminController : ControllerBase
{
    private readonly GetDashboardQueryHandler _getDashboardHandler;
    private readonly GetReconciliationQueryHandler _getReconciliationHandler;
    private readonly GetAdminPurchasesQueryHandler _getAdminPurchasesHandler;

    public AdminController(
        GetDashboardQueryHandler getDashboardHandler,
        GetReconciliationQueryHandler getReconciliationHandler,
        GetAdminPurchasesQueryHandler getAdminPurchasesHandler)
    {
        _getDashboardHandler = getDashboardHandler;
        _getReconciliationHandler = getReconciliationHandler;
        _getAdminPurchasesHandler = getAdminPurchasesHandler;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(GetDashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var result = await _getDashboardHandler.HandleAsync(new GetDashboardQuery(), cancellationToken);
        return Ok(result);
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
