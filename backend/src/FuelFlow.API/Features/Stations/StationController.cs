using FuelFlow.Features.Stations.GetPublicFuelTypes;
using FuelFlow.Features.Stations.GetPublicStations;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

// Anonymous and cached for 300s. Both actions project explicit DTOs rather than returning entities,
// so a column added to stations or fuel_types cannot become public reference data by accident (FF-31).
[ApiController]
[ResponseCache(Duration = 300)]
[Route("api/stations")]
public sealed class StationController : ControllerBase
{
    private readonly GetPublicStationsQueryHandler _handler;
    private readonly GetPublicFuelTypesQueryHandler _fuelTypesHandler;

    public StationController(
        GetPublicStationsQueryHandler handler,
        GetPublicFuelTypesQueryHandler fuelTypesHandler)
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
        Ok(await _fuelTypesHandler.HandleAsync(new GetPublicFuelTypesQuery(), ct));
}
