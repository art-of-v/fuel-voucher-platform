using System.Text;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Stations.CreateStationNode;
using FuelFlow.Features.Stations.DeleteStationNode;
using FuelFlow.Features.Stations.GetAdminStationNodeById;
using FuelFlow.Features.Stations.GetAdminStationNodes;
using FuelFlow.Features.Stations.ImportStationNodes;
using FuelFlow.Features.Stations.UpdateStationNode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/admin/station-nodes")]
[Authorize(Policy = "Staff")]
public sealed class AdminStationNodeController : ControllerBase
{
    private readonly GetAdminStationNodesQueryHandler _getAll;
    private readonly GetAdminStationNodeByIdQueryHandler _getById;
    private readonly CreateStationNodeCommandHandler _create;
    private readonly UpdateStationNodeCommandHandler _update;
    private readonly DeleteStationNodeCommandHandler _delete;
    private readonly ImportStationNodesCommandHandler _import;

    public AdminStationNodeController(
        GetAdminStationNodesQueryHandler getAll,
        GetAdminStationNodeByIdQueryHandler getById,
        CreateStationNodeCommandHandler create,
        UpdateStationNodeCommandHandler update,
        DeleteStationNodeCommandHandler delete,
        ImportStationNodesCommandHandler import)
    {
        _getAll = getAll;
        _getById = getById;
        _create = create;
        _update = update;
        _delete = delete;
        _import = import;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? stationId = null,
        CancellationToken ct = default) =>
        Ok(await _getAll.HandleAsync(new GetAdminStationNodesQuery(page, pageSize, stationId), ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetAdminStationNodeByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] StationNode request, CancellationToken ct)
    {
        var result = await _create.HandleAsync(new CreateStationNodeCommand(request), ct);
        if (result.Error != null && !result.Conflict)
            return BadRequest(result.Error);
        if (result.Conflict)
            return Conflict(result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Node!.Id }, result.Node);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] StationNode request, CancellationToken ct)
    {
        var result = await _update.HandleAsync(new UpdateStationNodeCommand(id, request), ct);
        if (result.NotFound) return NotFound();
        if (result.Error != null) return BadRequest(result.Error);
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken ct)
    {
        var success = await _delete.HandleAsync(new DeleteStationNodeCommand(id), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }

    [HttpPost("import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("A non-empty file is required");

        var format = (Path.GetExtension(file.FileName) ?? string.Empty).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? StationNodeImportFormat.Json
            : StationNodeImportFormat.Csv;

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            content = await reader.ReadToEndAsync(ct);

        var result = await _import.HandleAsync(new ImportStationNodesCommand(content, format), ct);
        return Ok(result);
    }
}
