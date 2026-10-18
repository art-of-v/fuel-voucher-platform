using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Brands.DeleteFuelType;
using FuelFlow.Features.Brands.GetAdminFuelTypeById;
using FuelFlow.Features.Brands.GetAdminFuelTypes;
using FuelFlow.Features.Brands.UpdateFuelType;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

[ApiController]
[Route("api/admin/fuel-types")]
public sealed class AdminFuelTypeController : ControllerBase
{
    private readonly GetAdminFuelTypesQueryHandler _getAll;
    private readonly GetAdminFuelTypeByIdQueryHandler _getById;
    private readonly UpdateFuelTypeCommandHandler _update;
    private readonly DeleteFuelTypeCommandHandler _delete;

    public AdminFuelTypeController(
        GetAdminFuelTypesQueryHandler getAll,
        GetAdminFuelTypeByIdQueryHandler getById,
        UpdateFuelTypeCommandHandler update,
        DeleteFuelTypeCommandHandler delete)
    {
        _getAll = getAll;
        _getById = getById;
        _update = update;
        _delete = delete;
    }

    [HttpGet]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminFuelTypesQuery(), ct));

    // Routes restored 2026-10-12. These were disabled by commenting out the attributes, which
    // disables nothing: under [ApiController] a method with no HTTP attribute binds to the
    // controller prefix, so four actions claimed `api/admin/fuel-types` and the router answered
    // 500 (AmbiguousMatchException) instead of 404. `GET /api/admin/fuel-types` has a live caller -
    // admin.tsx:380, the fuel-type list the voucher importer reads - so it was never dead.
    [HttpGet("{id}")]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetAdminFuelTypeByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    // POST removed 2026-10-18. It was the way one grade of a fuel got typed in twice: the create
    // handler refused only a duplicate Id and knew nothing about the brand, so `А95` and `А 95+` at
    // KLO, and `A-95` and `upg95` at UPG, all went in under fresh ids and neither DB index could
    // object — `ux_fuel_types_station_name` only fires on an identical name, and the whole point was
    // that the names differed.
    //
    // Creation now lives in two places that both know the brand: `POST /api/admin/brand-fuels/brands/{id}/fuels`,
    // which refuses a duplicate name and a duplicate code within the brand, and the UPG/WOG price
    // import, which keys on the brand's own published code. This route had no caller in any client,
    // script, deploy config or doc.
    //
    // The action is deleted rather than commented out on purpose. Under [ApiController] a method with
    // no HTTP attribute binds to the controller prefix instead of disappearing, which is how four
    // actions once answered 500 (AmbiguousMatchException) instead of 404 — see the note above.

    [HttpPut("{id}")]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] FuelTypeEntity request, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var success = await _update.HandleAsync(new UpdateFuelTypeCommand(id, request, userId), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken ct)
    {
        var success = await _delete.HandleAsync(new DeleteFuelTypeCommand(id), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }
}
