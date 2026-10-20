using FuelFlow.Features.Brands.GetAdminFuelTypeById;
using FuelFlow.Features.Brands.GetAdminFuelTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

/// <summary>
/// The legacy fuel-type surface. <b>Read-only.</b>
/// </summary>
/// <remarks>
/// <para>
/// Every write here is gone as of 2026-10-18. What remains is a list, because
/// <c>admin.tsx</c> reads it for the voucher importer's fuel-type names.
/// </para>
/// <para>
/// <b>POST</b> created the duplicate. It refused only a duplicate Id and knew nothing about the
/// brand, so <c>А95</c> and <c>А 95+</c> at KLO, and <c>A-95</c> and <c>upg95</c> at UPG, all went in
/// under fresh ids. Neither DB index could object: <c>ux_fuel_types_station_name</c> fires only on an
/// identical name, and the whole point was that the names differed.
/// </para>
/// <para>
/// <b>PUT</b> wrote Name, BrandId and BrandFuelCode with no check of any kind, so it could move a
/// fuel into another brand where that grade already existed. The unique index did catch the
/// collision - but at SaveChanges, so the operator saw a 500 rather than a 409, because
/// <c>GlobalExceptionHandler</c> has no <c>DbUpdateException</c> arm.
/// </para>
/// <para>
/// <b>DELETE</b> archived a fuel with none of the checks <c>BrandFuelController.DeleteFuel</c>
/// performs (offer and volume cleanup, the blocking-status guard).
/// </para>
/// <para>
/// None of the three had a caller in any client, script, deploy config or doc. Creation and editing
/// live in <c>BrandFuelController</c>, which checks within the brand; deletion lives there too.
/// </para>
/// <para>
/// The actions are deleted rather than commented out on purpose. Under <c>[ApiController]</c> a
/// method with no HTTP attribute binds to the controller prefix instead of disappearing, which is how
/// four actions here once answered 500 (AmbiguousMatchException) instead of 404.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/fuel-types")]
public sealed class AdminFuelTypeController : ControllerBase
{
    private readonly GetAdminFuelTypesQueryHandler _getAll;
    private readonly GetAdminFuelTypeByIdQueryHandler _getById;

    public AdminFuelTypeController(
        GetAdminFuelTypesQueryHandler getAll,
        GetAdminFuelTypeByIdQueryHandler getById)
    {
        _getAll = getAll;
        _getById = getById;
    }

    [HttpGet]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminFuelTypesQuery(), ct));

    [HttpGet("{id}")]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetAdminFuelTypeByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }
}