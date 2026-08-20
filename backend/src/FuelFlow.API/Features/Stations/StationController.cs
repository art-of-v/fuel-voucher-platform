using FuelFlow.Features.Stations.GetAdminFuelTypes;
using FuelFlow.Features.Stations.GetPublicStations;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

[ApiController]
[ResponseCache(Duration = 300)]
[Route("api/stations")]
public sealed class StationController : ControllerBase
{
    private readonly GetPublicStationsQueryHandler _handler;
    private readonly GetAdminFuelTypesQueryHandler _fuelTypesHandler;

    public StationController(
        GetPublicStationsQueryHandler handler,
        GetAdminFuelTypesQueryHandler fuelTypesHandler)
    {
        _handler = handler;
        _fuelTypesHandler = fuelTypesHandler;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _handler.HandleAsync(new GetPublicStationsQuery(), ct));

    [HttpGet("fuel-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFuelTypes(CancellationToken ct) =>
        Ok(await _fuelTypesHandler.HandleAsync(new GetAdminFuelTypesQuery(), ct));
}
