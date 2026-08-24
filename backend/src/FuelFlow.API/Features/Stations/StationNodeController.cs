using FuelFlow.Features.Stations.GetPublicStationNodes;
using FuelFlow.Features.Stations.GetPublicStationNodesByStation;
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
    private readonly GetPublicStationNodesByStationQueryHandler _getByStation;

    public StationNodeController(
        GetPublicStationNodesQueryHandler getAll,
        GetPublicStationNodesByStationQueryHandler getByStation)
    {
        _getAll = getAll;
        _getByStation = getByStation;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetPublicStationNodesQuery(), ct));

    [HttpGet("station/{stationId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByStation([FromRoute] string stationId, CancellationToken ct) =>
        Ok(await _getByStation.HandleAsync(new GetPublicStationNodesByStationQuery(stationId), ct));
}
