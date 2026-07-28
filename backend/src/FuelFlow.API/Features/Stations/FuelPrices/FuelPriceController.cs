using FuelFlow.Features.Stations.FuelPrices.BulkUpdateFuelPrices;
using FuelFlow.Features.Stations.FuelPrices.GetFuelPriceAudit;
using FuelFlow.Features.Stations.FuelPrices.GetFuelPrices;
using FuelFlow.Features.Stations.FuelPrices.UpdateFuelPrice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Stations.FuelPrices;

[ApiController]
[Route("api/admin/fuel-prices")]
[Authorize(Roles = "Admin")]
public sealed class FuelPriceController : ControllerBase
{
    private readonly GetFuelPricesQueryHandler _getAll;
    private readonly UpdateFuelPriceCommandHandler _update;
    private readonly BulkUpdateFuelPricesCommandHandler _bulkUpdate;
    private readonly GetFuelPriceAuditQueryHandler _getAudit;

    public FuelPriceController(
        GetFuelPricesQueryHandler getAll,
        UpdateFuelPriceCommandHandler update,
        BulkUpdateFuelPricesCommandHandler bulkUpdate,
        GetFuelPriceAuditQueryHandler getAudit)
    {
        _getAll = getAll;
        _update = update;
        _bulkUpdate = bulkUpdate;
        _getAudit = getAudit;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<FuelPriceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetFuelPricesQuery(), ct));

    [HttpPatch("{packageId}")]
    [ProducesResponseType(typeof(FuelPriceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] string packageId, [FromBody] UpdateFuelPriceRequest request, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var command = new UpdateFuelPriceCommand(
            packageId,
            userId,
            request.SupplierPricePerLiter,
            request.MarginUahPerLiter,
            request.MarginPercent,
            request.FinalPricePerLiter
        );

        var (result, error) = await _update.HandleAsync(command, ct);
        if (result is null && error == "Package not found") return NotFound();
        if (error is not null) return BadRequest(new { message = error });
        return Ok(result);
    }

    [HttpPatch("bulk")]
    [ProducesResponseType(typeof(BulkUpdateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkUpdate([FromBody] BulkUpdateFuelPricesRequest request, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var patches = request.Patches.Select(p => new BulkPricePatch(
            p.PackageId,
            p.SupplierPricePerLiter,
            p.MarginUahPerLiter,
            p.MarginPercent,
            p.FinalPricePerLiter
        )).ToList();

        var result = await _bulkUpdate.HandleAsync(new BulkUpdateFuelPricesCommand(userId, patches), ct);
        return Ok(result);
    }

    [HttpGet("audit")]
    [ProducesResponseType(typeof(List<FuelPriceAuditDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAudit([FromQuery] string? packageId, [FromQuery] int limit = 100, CancellationToken ct = default) =>
        Ok(await _getAudit.HandleAsync(new GetFuelPriceAuditQuery(packageId, Math.Min(limit, 500)), ct));
}

// Request DTOs
public sealed class UpdateFuelPriceRequest
{
    public decimal? SupplierPricePerLiter { get; set; }
    public decimal? MarginUahPerLiter { get; set; }
    public decimal? MarginPercent { get; set; }
    public decimal? FinalPricePerLiter { get; set; }
}

public sealed class BulkUpdateFuelPricesRequest
{
    public List<BulkPricePatchRequest> Patches { get; set; } = new();
}

public sealed class BulkPricePatchRequest
{
    public string PackageId { get; set; } = null!;
    public decimal? SupplierPricePerLiter { get; set; }
    public decimal? MarginUahPerLiter { get; set; }
    public decimal? MarginPercent { get; set; }
    public decimal? FinalPricePerLiter { get; set; }
}
