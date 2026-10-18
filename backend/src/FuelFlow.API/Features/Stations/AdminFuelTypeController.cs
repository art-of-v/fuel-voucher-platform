using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Brands.CreateFuelType;
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
    private readonly CreateFuelTypeCommandHandler _create;
    private readonly UpdateFuelTypeCommandHandler _update;
    private readonly DeleteFuelTypeCommandHandler _delete;

    public AdminFuelTypeController(
        GetAdminFuelTypesQueryHandler getAll,
        GetAdminFuelTypeByIdQueryHandler getById,
        CreateFuelTypeCommandHandler create,
        UpdateFuelTypeCommandHandler update,
        DeleteFuelTypeCommandHandler delete)
    {
        _getAll = getAll;
        _getById = getById;
        _create = create;
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

    [HttpPost]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> Create([FromBody] FuelTypeEntity request, CancellationToken ct)
    {
        var result = await _create.HandleAsync(new CreateFuelTypeCommand(request), ct);
        if (result.Error != null && !result.Conflict)
            return BadRequest(result.Error);
        if (result.Conflict)
            return Conflict(result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.FuelType!.Id }, result.FuelType);
    }

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
