using FuelFlow.Features.Stations.GetPublicStations;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

[ApiController]
[ResponseCache(Duration = 300)]
[Route("api/stations")]
public sealed class StationController : ControllerBase
{
    private readonly GetPublicStationsQueryHandler _handler;

    public StationController(GetPublicStationsQueryHandler handler) => _handler = handler;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _handler.HandleAsync(new GetPublicStationsQuery(), ct));
}
