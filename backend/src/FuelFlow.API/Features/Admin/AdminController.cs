using FuelFlow.Features.Admin.GetDashboard;
using FuelFlow.Features.Admin.GetRealizedMargin;
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
    private readonly GetDashboardQueryHandler _getDashboardHandler;
    private readonly GetReconciliationQueryHandler _getReconciliationHandler;
    private readonly GetRealizedMarginQueryHandler _getRealizedMarginHandler;
    private readonly GetAdminPurchasesQueryHandler _getAdminPurchasesHandler;

    public AdminController(
        GetDashboardQueryHandler getDashboardHandler,
        GetReconciliationQueryHandler getReconciliationHandler,
        GetRealizedMarginQueryHandler getRealizedMarginHandler,
        GetAdminPurchasesQueryHandler getAdminPurchasesHandler)
    {
        _getDashboardHandler = getDashboardHandler;
        _getReconciliationHandler = getReconciliationHandler;
        _getRealizedMarginHandler = getRealizedMarginHandler;
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

    /// <summary>
    /// What we actually earned against the cost of the vouchers actually handed over. This is the
    /// view that can prove a loss; the reconciliation above estimates margin from the live catalogue
    /// and cannot.
    /// </summary>
    [HttpGet("realized-margin")]
    [ProducesResponseType(typeof(RealizedMarginReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRealizedMargin(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _getRealizedMarginHandler.HandleAsync(
            new GetRealizedMarginQuery(fromDate, toDate, limit), cancellationToken);
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
