using System.Text;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Brands.CreateBrandStation;
using FuelFlow.Features.Brands.DeleteBrandStation;
using FuelFlow.Features.Brands.GetAdminBrandStationById;
using FuelFlow.Features.Brands.GetAdminBrandStations;
using FuelFlow.Features.Brands.ImportBrandStations;
using FuelFlow.Features.Brands.UpdateBrandStation;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

[ApiController]
[Route("api/admin/brand-stations")]
public sealed class AdminBrandStationController : ControllerBase
{
    private readonly GetAdminBrandStationsQueryHandler _getAll;
    private readonly GetAdminBrandStationByIdQueryHandler _getById;
    private readonly CreateBrandStationCommandHandler _create;
    private readonly UpdateBrandStationCommandHandler _update;
    private readonly DeleteBrandStationCommandHandler _delete;
    private readonly ImportBrandStationsCommandHandler _import;

    public AdminBrandStationController(
        GetAdminBrandStationsQueryHandler getAll,
        GetAdminBrandStationByIdQueryHandler getById,
        CreateBrandStationCommandHandler create,
        UpdateBrandStationCommandHandler update,
        DeleteBrandStationCommandHandler delete,
        ImportBrandStationsCommandHandler import)
    {
        _getAll = getAll;
        _getById = getById;
        _create = create;
        _update = update;
        _delete = delete;
        _import = import;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? BrandId = null,
        CancellationToken ct = default) =>
        Ok(await _getAll.HandleAsync(new GetAdminBrandStationsQuery(page, pageSize, BrandId), ct));

    [HttpGet("{id}")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetAdminBrandStationByIdQuery(id), ct);
        return result is null ? ApiError.NotFound("Not found") : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Create([FromBody] BrandStation request, CancellationToken ct)
    {
        var result = await _create.HandleAsync(new CreateBrandStationCommand(request), ct);
        if (result.Error != null && !result.Conflict)
            return ApiError.BadRequest(result.Error, result.Code);
        if (result.Conflict)
            return ApiError.Conflict(result.Error, result.Code);
        return CreatedAtAction(nameof(GetById), new { id = result.Node!.Id }, result.Node);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] BrandStation request, CancellationToken ct)
    {
        var result = await _update.HandleAsync(new UpdateBrandStationCommand(id, request), ct);
        if (result.NotFound) return ApiError.NotFound("Not found", result.Code);
        if (result.Error != null) return ApiError.BadRequest(result.Error, result.Code);
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var success = await _delete.HandleAsync(new DeleteBrandStationCommand(id), ct);
        return success
            ? Ok(new { success = true })
            : ApiError.NotFound("Not found", BrandStationRefusalCodes.NotFound);
    }

    [HttpPost("import")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    // The multipart reader's ceiling is global, so a route that accepts a file this size raises
    // that one too - otherwise the reader refuses the body the route accepts.
    [RequestFormLimits(MultipartBodyLengthLimit = 20 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return ApiError.BadRequest("A non-empty file is required");

        var format = (Path.GetExtension(file.FileName) ?? string.Empty).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? BrandStationImportFormat.Json
            : BrandStationImportFormat.Csv;

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            content = await reader.ReadToEndAsync(ct);

        var result = await _import.HandleAsync(new ImportBrandStationsCommand(content, format), ct);
        return Ok(result);
    }
}
