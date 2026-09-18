using FuelFlow.Features.Report.GetReport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Report;

[ApiController]
[Route("api/admin/report")]
[Authorize(Policy = "Staff")]
public sealed class AdminReportController : ControllerBase
{
    private readonly GetReportQueryHandler _getReportHandler;
    private readonly ILogger<AdminReportController> _logger;

    public AdminReportController(
        GetReportQueryHandler getReportHandler,
        ILogger<AdminReportController> logger)
    {
        _getReportHandler = getReportHandler;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserReport(
        [FromQuery] Guid? userId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new GetReportQuery(userId, fromDate, toDate);
            var response = await _getReportHandler.HandleAsync(query, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating admin report for user {UserId}", userId);
            return StatusCode(500, "An error occurred while generating report");
        }
    }
}
