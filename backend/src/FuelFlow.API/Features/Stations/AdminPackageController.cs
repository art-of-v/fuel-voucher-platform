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
[Authorize(Policy = "Staff")]
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

    // Removed 2026-10-12 (planning #289): no caller in any client, script, deploy config, doc or
    // test. They were previously "disabled" by commenting out the [HttpGet] attribute, which does
    // not disable an action - under [ApiController] a method with no HTTP attribute binds to the
    // controller prefix, so four actions here plus GetAll all claimed `api/admin/packages` and the
    // router answered 500 (AmbiguousMatchException) instead of 404. Deleting the method is the way
    // to remove an endpoint; commenting the attribute out is not.

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
}
