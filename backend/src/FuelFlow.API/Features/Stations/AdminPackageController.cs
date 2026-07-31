using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Stations.CreatePackage;
using FuelFlow.Features.Stations.DeletePackage;
using FuelFlow.Features.Stations.GetAdminPackages;
using FuelFlow.Features.Stations.GetAdminPackagesByStation;
using FuelFlow.Features.Stations.GetPackageSuggestions;
using FuelFlow.Features.Stations.UpdatePackage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/admin/packages")]
[Authorize(Roles = "Admin")]
public sealed class AdminPackageController : ControllerBase
{
    private readonly GetAdminPackagesQueryHandler _getAll;
    private readonly GetAdminPackagesByStationQueryHandler _getByStation;
    private readonly GetPackageSuggestionsQueryHandler _getSuggestions;
    private readonly CreatePackageCommandHandler _create;
    private readonly UpdatePackageCommandHandler _update;
    private readonly DeletePackageCommandHandler _delete;

    public AdminPackageController(
        GetAdminPackagesQueryHandler getAll,
        GetAdminPackagesByStationQueryHandler getByStation,
        GetPackageSuggestionsQueryHandler getSuggestions,
        CreatePackageCommandHandler create,
        UpdatePackageCommandHandler update,
        DeletePackageCommandHandler delete)
    {
        _getAll = getAll;
        _getByStation = getByStation;
        _getSuggestions = getSuggestions;
        _create = create;
        _update = update;
        _delete = delete;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminPackagesQuery(), ct));

    [HttpGet("station/{stationId}")]
    public async Task<IActionResult> GetByStation([FromRoute] string stationId, CancellationToken ct) =>
        Ok(await _getByStation.HandleAsync(new GetAdminPackagesByStationQuery(stationId), ct));

    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions(CancellationToken ct) =>
        Ok(await _getSuggestions.HandleAsync(new GetPackageSuggestionsQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FuelPackage request, CancellationToken ct)
    {
        var result = await _create.HandleAsync(new CreatePackageCommand(request), ct);
        if (result.Error != null && !result.Conflict)
            return BadRequest(result.Error);
        if (result.Conflict)
            return Conflict(result.Error);
        return CreatedAtAction(nameof(GetAll), new { id = result.Package!.Id }, result.Package);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] FuelPackage request, CancellationToken ct)
    {
        var success = await _update.HandleAsync(new UpdatePackageCommand(id, request), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken ct)
    {
        var success = await _delete.HandleAsync(new DeletePackageCommand(id), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }
}
