using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Stations.CreateStation;
using FuelFlow.Features.Stations.DeleteStation;
using FuelFlow.Features.Stations.GetAdminStationById;
using FuelFlow.Features.Stations.GetAdminStations;
using FuelFlow.Features.Stations.UpdateStation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/admin/stations")]
[Authorize(Roles = "Admin")]
public sealed class AdminStationController : ControllerBase
{
    private readonly GetAdminStationsQueryHandler _getAll;
    private readonly GetAdminStationByIdQueryHandler _getById;
    private readonly CreateStationCommandHandler _create;
    private readonly UpdateStationCommandHandler _update;
    private readonly DeleteStationCommandHandler _delete;

    public AdminStationController(
        GetAdminStationsQueryHandler getAll,
        GetAdminStationByIdQueryHandler getById,
        CreateStationCommandHandler create,
        UpdateStationCommandHandler update,
        DeleteStationCommandHandler delete)
    {
        _getAll = getAll;
        _getById = getById;
        _create = create;
        _update = update;
        _delete = delete;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminStationsQuery(), ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetAdminStationByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Station request, CancellationToken ct)
    {
        var result = await _create.HandleAsync(new CreateStationCommand(request), ct);
        if (result.Error != null && !result.Conflict)
            return BadRequest(result.Error);
        if (result.Conflict)
            return Conflict(result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Station!.Id }, result.Station);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] Station request, CancellationToken ct)
    {
        var success = await _update.HandleAsync(new UpdateStationCommand(id, request), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken ct)
    {
        var success = await _delete.HandleAsync(new DeleteStationCommand(id), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }
}
