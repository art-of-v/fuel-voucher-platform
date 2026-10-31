using System.Security.Claims;
using FuelFlow.Features.Brands.AdminAddFuel;
using FuelFlow.Features.Brands.AdminDeleteFuel;
using FuelFlow.Features.Brands.AdminUpdateFuel;
using FuelFlow.Features.Brands.AdminUpdateNominals;
using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Brands;

/// <summary>
/// What a brand sells: its fuels, what each is priced at, and which nominal sizes exist.
/// <para>
/// <b>Split out of <see cref="BrandsController"/>, which is about the brand itself.</b> For nine
/// endpoints the file was called <c>BrandsController</c> while half of it priced fuel - the same name
/// covering "this network exists" and "this litre sells for 54.90", which is how <c>Brands</c> ended
/// up read as a filling Brand rather than a brand. A margin written here is a decision about a grade,
/// not about a network, so the routes and the name say so.
/// </para>
/// <para>
/// Route is <c>api/admin/brand-fuels</c> rather than the old nested <c>api/admin/brands/fuels/...</c>:
/// a fuel belongs to a brand but is addressed on its own - the fuel id is enough to price it, and a
/// caller holding it should not also have to know which brand it hangs under.
/// </para>
/// <para>
/// As in the brand controller, the rules live in the handlers: which fuels may be added, what a
/// repricing may and may not change, and when a fuel may be archived are all decisions the
/// database cannot make on its own, and none of them can be tested without an HTTP pipeline around
/// them while they sit in an action method.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/brand-fuels")]
public sealed class BrandFuelController : ControllerBase
{
    private readonly GetFuelPriceHistoryQueryHandler _getPriceHistory;
    private readonly GetFuelPriceAtQueryHandler _getPriceAt;
    private readonly AdminAddFuelCommandHandler _addFuel;
    private readonly AdminUpdateFuelCommandHandler _updateFuel;
    private readonly AdminDeleteFuelCommandHandler _deleteFuel;
    private readonly AdminUpdateNominalsCommandHandler _updateNominals;

    public BrandFuelController(
        GetFuelPriceHistoryQueryHandler getPriceHistory,
        GetFuelPriceAtQueryHandler getPriceAt,
        AdminAddFuelCommandHandler addFuel,
        AdminUpdateFuelCommandHandler updateFuel,
        AdminDeleteFuelCommandHandler deleteFuel,
        AdminUpdateNominalsCommandHandler updateNominals)
    {
        _getPriceHistory = getPriceHistory;
        _getPriceAt = getPriceAt;
        _addFuel = addFuel;
        _updateFuel = updateFuel;
        _deleteFuel = deleteFuel;
        _updateNominals = updateNominals;
    }

    /// <summary>
    /// What one fuel was priced at, oldest first. The numbers the dashboard and reconciliation apply to
    /// historical orders, against the sequence that produced them - see
    /// <see cref="GetFuelPriceHistoryQueryHandler"/>.
    /// </summary>
    [HttpGet("{fuelId}/price-history")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetFuelPriceHistory(
        [FromRoute] Guid fuelId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await _getPriceHistory.HandleAsync(new GetFuelPriceHistoryQuery(fuelId, limit), ct));

    /// <summary>
    /// What one fuel was priced at at a given instant — the question an order line from last month asks,
    /// which a series clamped to the newest <c>limit</c> rows cannot answer.
    /// <para>
    /// Returns null (200 with an empty body) when no price had taken effect by then, which is a real
    /// answer: the fuel did not exist, or was not priced yet. Not a 404 — nothing is missing here.
    /// </para>
    /// </summary>
    [HttpGet("{fuelId}/price-at")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetFuelPriceAt(
        [FromRoute] Guid fuelId,
        [FromQuery] DateTime asOf,
        CancellationToken ct = default) =>
        Ok(await _getPriceAt.HandleAsync(new GetFuelPriceAtQuery(fuelId, asOf), ct));
    [HttpPost("brands/{id}/fuels")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> AddFuel([FromRoute] Guid id, [FromBody] CreateFuelRequest request, CancellationToken ct)
    {
        var result = await _addFuel.HandleAsync(
            new AdminAddFuelCommand(id, request, GetUserId(), GetUserName()), ct);

        if (result.Success)
            return Created($"/api/admin/brand-fuels/{result.CreatedFuelId}", new { id = result.CreatedFuelId });

        if (result.NotFound)
            return ApiError.NotFound("Not found", BrandRefusalCodes.BrandNotFound);

        // The refusal that names the fuel that already holds the name or the code carries its id, so
        // the operator is told which row to reprice. The refusal that names no fuel does not - its
        // body has never had the field, and growing a null one would change the answer.
        if (result.Conflict)
            return result.FuelTypeId is null
                ? ApiError.Conflict(result.Error, result.Code, extra: Extensions(("field", result.Field)))
                : ApiError.Conflict(result.Error, result.Code, extra: Extensions(
                    ("fuelTypeId", result.FuelTypeId), ("field", result.Field), ("twinName", result.TwinName)));

        return ApiError.BadRequest(result.Error, result.Code, extra: Extensions(
            ("field", result.Field), ("token", result.Token)));
    }

    [HttpPut("{fuelId}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> UpdateFuel([FromRoute] Guid fuelId, [FromBody] BrandFuelDto request, CancellationToken ct)
    {
        var result = await _updateFuel.HandleAsync(
            new AdminUpdateFuelCommand(fuelId, request, GetUserId(), GetUserName()), ct);

        if (result.Success)
            return Ok(new { success = true });

        if (result.NotFound)
            return ApiError.NotFound("Not found", BrandRefusalCodes.FuelNotFound);

        // The refusal that names the fuel that already holds the code carries its id. The refusal
        // that names no fuel - the one about the pool - does not, and its body has never had the
        // field, and growing a null one would change the answer.
        if (result.Conflict)
            return result.FuelTypeId is null
                ? ApiError.Conflict(result.Error, result.Code, extra: Extensions(("field", result.Field)))
                : ApiError.Conflict(result.Error, result.Code, extra: Extensions(
                    ("fuelTypeId", result.FuelTypeId), ("field", result.Field)));

        return ApiError.BadRequest(result.Error, result.Code, extra: Extensions(
            ("field", result.Field), ("token", result.Token), ("gap", result.Gap)));
    }

    [HttpDelete("{fuelId}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> DeleteFuel([FromRoute] Guid fuelId, CancellationToken ct)
    {
        var result = await _deleteFuel.HandleAsync(
            new AdminDeleteFuelCommand(fuelId, GetUserId(), GetUserName()), ct);

        if (result.Success)
            return Ok(new { success = true });

        if (result.NotFound)
            return ApiError.NotFound("Not found", BrandRefusalCodes.FuelNotFound);

        // The refusal carries the count of the vouchers that block the archive and a few of them: the
        // operator needs the size of the problem and not only the first rows of it.
        return ApiError.Conflict(
            result.Error,
            result.Code,
            extra: Extensions(
                ("activeVoucherCount", result.ActiveVoucherCount),
                ("examples", result.Examples.Select(v => new { v.VoucherNumber, status = v.Status, v.Liters }))));
    }

    [HttpPut("brands/{id}/nominals")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> UpdateNominals([FromRoute] Guid id, [FromBody] List<int> nominals, CancellationToken ct)
    {
        var result = await _updateNominals.HandleAsync(
            new AdminUpdateNominalsCommand(id, nominals, GetUserId(), GetUserName()), ct);

        if (result.Success)
            return Ok(new { success = true });

        if (result.NotFound)
            return ApiError.NotFound("Not found", BrandRefusalCodes.BrandNotFound);

        return ApiError.BadRequest(
            result.Error,
            result.Code,
            extra: Extensions(("invalid", result.Invalid)));
    }

    /// <summary>
    /// The machine-readable half of a refusal, as the extensions of the one error envelope. A null
    /// value is left out: the answer that never carried the field must not grow one.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> Extensions(params (string Key, object? Value)[] pairs)
    {
        var extensions = new Dictionary<string, object?>();
        foreach (var (key, value) in pairs)
        {
            if (value is not null) extensions[key] = value;
        }
        return extensions;
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
        if (first is not null && last is not null) return $"{first} {last}";
        if (first is not null) return first;
        if (last is not null) return last;
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}
