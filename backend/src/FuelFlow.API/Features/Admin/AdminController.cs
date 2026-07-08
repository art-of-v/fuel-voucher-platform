using FuelFlow.Features.Admin.GetDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public sealed class AdminController : ControllerBase
{
    private readonly GetDashboardQueryHandler _getDashboardHandler;

    public AdminController(GetDashboardQueryHandler getDashboardHandler)
    {
        _getDashboardHandler = getDashboardHandler;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(GetDashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var result = await _getDashboardHandler.HandleAsync(new GetDashboardQuery(), cancellationToken);
        return Ok(result);
    }
}
