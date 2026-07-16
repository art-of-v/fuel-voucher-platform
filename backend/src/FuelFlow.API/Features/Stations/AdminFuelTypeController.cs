using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Stations.CreateFuelType;
using FuelFlow.Features.Stations.DeleteFuelType;
using FuelFlow.Features.Stations.GetAdminFuelTypeById;
using FuelFlow.Features.Stations.GetAdminFuelTypes;
using FuelFlow.Features.Stations.UpdateFuelType;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

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
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminFuelTypesQuery(), ct));

    [HttpGet("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetAdminFuelTypeByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] FuelTypeEntity request, CancellationToken ct)
    {
        var success = await _update.HandleAsync(new UpdateFuelTypeCommand(id, request), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken ct)
    {
        var success = await _delete.HandleAsync(new DeleteFuelTypeCommand(id), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }
}
