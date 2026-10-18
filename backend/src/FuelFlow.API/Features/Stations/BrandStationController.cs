using FuelFlow.Features.Brands.GetPublicBrandStations;
using FuelFlow.Features.Brands.GetPublicBrandStationsByBrand;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

// Public Brand map data. [AllowAnonymous] is explicit because AuthSetup installs a
// RequireAuthenticatedUser FallbackPolicy - an endpoint with no attribute now answers 401.
[ApiController]
[AllowAnonymous]
[ResponseCache(Duration = 300)]
[Route("api/brand-stations")]
public sealed class BrandStationController : ControllerBase
{
    private readonly GetPublicBrandStationsQueryHandler _getAll;
    private readonly GetPublicBrandStationsByBrandQueryHandler _getByBrand;

    public BrandStationController(
        GetPublicBrandStationsQueryHandler getAll,
        GetPublicBrandStationsByBrandQueryHandler getByBrand)
    {
        _getAll = getAll;
        _getByBrand = getByBrand;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetPublicBrandStationsQuery(), ct));

    [HttpGet("{brandId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBrand([FromRoute] string BrandId, CancellationToken ct) =>
        Ok(await _getByBrand.HandleAsync(new GetPublicBrandStationsByBrandQuery(BrandId), ct));
}