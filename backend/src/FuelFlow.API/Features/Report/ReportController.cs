using System.Security.Claims;
using FuelFlow.Features.Report.GetReport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Report;

[ApiController]
[Route("api/report")]
[Authorize]
public sealed class ReportController : ControllerBase
{
    private readonly GetReportQueryHandler _getReportHandler;
    private readonly ILogger<ReportController> _logger;

    public ReportController(
        GetReportQueryHandler getReportHandler,
        ILogger<ReportController> logger)
    {
        _getReportHandler = getReportHandler;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetReport(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("User ID not found in claims");
            return Unauthorized("User ID not found");
        }

        try
        {
            var query = new GetReportQuery(Guid.Parse(userId), fromDate, toDate);
            var response = await _getReportHandler.HandleAsync(query, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating report for user {UserId}", userId);
            return StatusCode(500, "An error occurred while generating report");
        }
    }
}
