using System.Security.Claims;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers.GetWorkerUsageReport;

/// <summary>
/// A worker's fuel usage for one company (multi-company epic #103, S5 - #150).
/// </summary>
/// <remarks>
/// The counterpart to the owner's view: a worker redeems but never buys, so what they need is not
/// stock or revenue but "what was I given, what have I used, and what is left".
/// </remarks>
[ApiController]
[Route("api/company/worker-usage")]
[Authorize]
public sealed class GetWorkerUsageReportController : ControllerBase
{
    private readonly GetWorkerUsageReportQueryHandler _handler;

    public GetWorkerUsageReportController(GetWorkerUsageReportQueryHandler handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// Received / used / remaining for every voucher a company has issued to the caller.
    /// </summary>
    /// <remarks>
    /// Scoped to the caller: a worker sees only their own vouchers, and only those issued by the
    /// company asked for. There is no way to read somebody else's.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetWorkerUsageReportResponse>> Get(
        [FromQuery] Guid legalEntityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (legalEntityId == Guid.Empty)
        {
            return BadRequest("legalEntityId is required");
        }

        // The page size is clamped by PagedRequest, so a client asking for
        // "?pageSize=2000000000" gets the maximum and not the whole table.
        var paging = new PagedRequest(Page: page, PageSize: pageSize);

        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _handler.HandleAsync(
            new GetWorkerUsageReportQuery(userId, legalEntityId, paging), cancellationToken);

        // The body keeps its shape - the app reads an object with a totals block and an items
        // list - and the count travels in a header, so a client can tell that another page
        // follows without the shape changing.
        Response.Headers["X-Total-Count"] = result.TotalCount.ToString();
        Response.Headers["X-Has-Next-Page"] = result.TotalCount > paging.Skip + result.Items.Count ? "true" : "false";

        return Ok(new GetWorkerUsageReportResponse(legalEntityId, result.Totals, result.Items));
    }

    /// <summary>
    /// The caller's user id. An authenticated request without a usable id claim is treated as
    /// unauthenticated rather than answered with somebody else's data.
    /// </summary>
    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
