using FuelFlow.Features.Stations.GetPublicStationNodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

// Public station map data. [AllowAnonymous] is explicit because AuthSetup installs a
// RequireAuthenticatedUser FallbackPolicy - an endpoint with no attribute now answers 401.
[ApiController]
[AllowAnonymous]
[ResponseCache(Duration = 300)]
[Route("api/station-nodes")]
public sealed class StationNodeController : ControllerBase
{
    private readonly GetPublicStationNodesQueryHandler _getAll;

    public StationNodeController(GetPublicStationNodesQueryHandler getAll)
    {
        _getAll = getAll;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetPublicStationNodesQuery(), ct));
}
