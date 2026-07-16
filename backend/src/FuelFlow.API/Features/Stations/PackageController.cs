using FuelFlow.Features.Stations.GetPublicPackages;
using FuelFlow.Features.Stations.GetPublicPackagesByStation;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/packages")]
public sealed class PackageController : ControllerBase
{
    private readonly GetPublicPackagesQueryHandler _getAll;
    private readonly GetPublicPackagesByStationQueryHandler _getByStation;

    public PackageController(
        GetPublicPackagesQueryHandler getAll,
        GetPublicPackagesByStationQueryHandler getByStation)
    {
        _getAll = getAll;
        _getByStation = getByStation;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetPublicPackagesQuery(), ct));

    [HttpGet("station/{stationId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByStation([FromRoute] string stationId, CancellationToken ct) =>
        Ok(await _getByStation.HandleAsync(new GetPublicPackagesByStationQuery(stationId), ct));
}
