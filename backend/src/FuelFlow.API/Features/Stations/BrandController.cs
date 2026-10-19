using System.Security.Claims;
using FuelFlow.Features.Brands.GetPublicFuelTypes;
using FuelFlow.Features.Brands.GetPublicBrands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

// Anonymous and cached for 300s. Both actions project explicit DTOs rather than returning entities,
// so a column added to Brands or fuel_types cannot become public reference data by accident (FF-31).
// [AllowAnonymous] is explicit rather than implied: the global FallbackPolicy in AuthSetup requires an
// authenticated user for every endpoint that does not opt out, so "no attribute" now means 401.
[ApiController]
[AllowAnonymous]
[ResponseCache(Duration = 300)]
[Route("api/brands")]
public sealed class BrandController : ControllerBase
{
    private readonly GetPublicBrandsQueryHandler _handler;
    private readonly GetPublicFuelTypesQueryHandler _fuelTypesHandler;

    public BrandController(
        GetPublicBrandsQueryHandler handler,
        GetPublicFuelTypesQueryHandler fuelTypesHandler)
    {
        _handler = handler;
        _fuelTypesHandler = fuelTypesHandler;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _handler.HandleAsync(new GetPublicBrandsQuery(), ct));

    [HttpGet("fuel-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFuelTypes(CancellationToken ct) =>
        Ok(await _fuelTypesHandler.HandleAsync(new GetPublicFuelTypesQuery(CallerUserId()), ct));
    /// <summary>
    /// Who is asking, so the fuel list can say whether each fuel can be bought BY THIS CALLER - the
    /// stock a QA account sees is the rehearsal pool, and the two are legitimately different.
    /// </summary>
    private Guid? CallerUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }}
