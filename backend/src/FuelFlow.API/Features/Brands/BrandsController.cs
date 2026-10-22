using System.Security.Claims;
using FuelFlow.Features.Brands.AdminCreateBrand;
using FuelFlow.Features.Brands.AdminDeleteBrand;
using FuelFlow.Features.Brands.AdminUpdateBrand;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

/// <summary>
/// The networks of the catalogue, for the staff surface.
/// </summary>
/// <remarks>
/// The controller reads the claims, asks a handler what it should do, and maps the answer onto the
/// response. It does not touch the database: every rule about what may be written, and what may not,
/// lives in the handler that owns the write - which is the only way those rules can be tested
/// without an HTTP pipeline around them.
/// </remarks>
[ApiController]
[Route("api/admin/brands")]
public sealed class BrandsController : ControllerBase
{
    private readonly GetBrandsQueryHandler _getAll;
    private readonly GetBrandByIdQueryHandler _getById;
    private readonly GetBrandHistoryQueryHandler _getHistory;
    private readonly AdminCreateBrandCommandHandler _create;
    private readonly AdminUpdateBrandCommandHandler _update;
    private readonly AdminDeleteBrandCommandHandler _delete;
    private readonly BrandSlugResolver _brands;

    public BrandsController(
        GetBrandsQueryHandler getAll,
        GetBrandByIdQueryHandler getById,
        GetBrandHistoryQueryHandler getHistory,
        AdminCreateBrandCommandHandler create,
        AdminUpdateBrandCommandHandler update,
        AdminDeleteBrandCommandHandler delete,
        BrandSlugResolver brands)
    {
        _getAll = getAll;
        _getById = getById;
        _getHistory = getHistory;
        _create = create;
        _update = update;
        _delete = delete;
        _brands = brands;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetBrandsQuery(), ct));

    [HttpGet("{id}")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetBrandByIdQuery(id), ct);
        return result is null ? ApiError.NotFound("Not found") : Ok(result);
    }

    /// <summary>
    /// The audit trail for one brand.
    /// </summary>
    /// <remarks>
    /// Scoped by <b>slug</b>, not id, and that is what keeps the history intact across the uuid
    /// migration: <c>audit_event_outbox.scope_id</c> has always held the network's own code, so the
    /// rows written before the id changed still resolve. Only <c>aggregate_id</c> held the old text
    /// id, and that is a label on the audit screen rather than the lookup - which is why nothing had
    /// to be rewritten to keep this endpoint working.
    /// </remarks>
    [HttpGet("{id}/history")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetHistory(
        [FromRoute] Guid id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var slug = await _brands.SlugOfAsync(id, ct);
        if (slug is null) return ApiError.NotFound("Not found");

        return Ok(await _getHistory.HandleAsync(new GetBrandHistoryQuery(slug, limit), ct));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Create([FromBody] BrandDto request, CancellationToken ct)
    {
        var result = await _create.HandleAsync(
            new AdminCreateBrandCommand(request, GetUserId(), GetUserName()), ct);

        if (result.Success)
            return CreatedAtAction(nameof(GetById), new { id = result.Brand!.Id }, result.Brand);

        // A slug that is taken is a clash with a row that already exists; a slug that is not there
        // at all is a request that never had a chance. The bodies differ, so the answers do too.
        return result.Conflict
            ? ApiError.Conflict(result.Error)
            : ApiError.BadRequest(result.Error);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] BrandDto request, CancellationToken ct)
    {
        var result = await _update.HandleAsync(
            new AdminUpdateBrandCommand(id, request, GetUserId(), GetUserName()), ct);

        return result.Found ? Ok(new { success = true }) : ApiError.NotFound("Not found");
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _delete.HandleAsync(
            new AdminDeleteBrandCommand(id, GetUserId(), GetUserName()), ct);

        return result.Found ? Ok(new { success = true }) : ApiError.NotFound("Not found");
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (first is not null && last is not null) return $"{first} ${last}";
        if (first is not null) return first;
        if (last is not null) return last;
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }

}
