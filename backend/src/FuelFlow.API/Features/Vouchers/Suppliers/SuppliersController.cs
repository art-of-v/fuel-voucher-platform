using System.Security.Claims;
using FuelFlow.Features.Vouchers.Suppliers.CreateSupplier;
using FuelFlow.Features.Vouchers.Suppliers.DeactivateSupplier;
using FuelFlow.Features.Vouchers.Suppliers.GetSuppliers;
using FuelFlow.Features.Vouchers.Suppliers.ReactivateSupplier;
using FuelFlow.Features.Vouchers.Suppliers.UpdateSupplier;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Vouchers.Suppliers;

/// <summary>
/// Suppliers — the external parties we buy vouchers from, as distinct from our own
/// <c>LegalEntity</c> companies. Staff only, because choosing a supplier on an import decides who the
/// stock is settled and exchanged with.
/// </summary>
[ApiController]
[Route("api/admin/suppliers")]
public sealed class SuppliersController : ControllerBase
{
    private readonly GetSuppliersQueryHandler _list;
    private readonly CreateSupplierCommandHandler _create;
    private readonly UpdateSupplierCommandHandler _update;
    private readonly DeactivateSupplierCommandHandler _deactivate;
    private readonly ReactivateSupplierCommandHandler _reactivate;

    public SuppliersController(
        GetSuppliersQueryHandler list,
        CreateSupplierCommandHandler create,
        UpdateSupplierCommandHandler update,
        DeactivateSupplierCommandHandler deactivate,
        ReactivateSupplierCommandHandler reactivate)
    {
        _list = list;
        _create = create;
        _update = update;
        _deactivate = deactivate;
        _reactivate = reactivate;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<ActionResult<List<SupplierDto>>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
        => Ok(await _list.HandleAsync(new GetSuppliersQuery(includeInactive), ct));

    /// <summary>The accepted legal forms, so the UI never invents one we cannot validate.</summary>
    [HttpGet("legal-forms")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public ActionResult<IReadOnlyList<string>> LegalFormOptions() => Ok(SupplierRules.LegalForms);

    /// <summary>
    /// The accepted supply routes, on the same terms as <see cref="LegalFormOptions"/>: the UI picks
    /// from what the server validates against rather than hardcoding a second list that can drift.
    /// </summary>
    [HttpGet("acquisition-sources")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public ActionResult<IReadOnlyList<string>> AcquisitionSourceOptions() => Ok(AcquisitionSources.All);

    [HttpPost]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<ActionResult<SupplierDto>> Create(CreateSupplierRequest request, CancellationToken ct = default)
    {
        var result = await _create.HandleAsync(
            new CreateSupplierCommand(request, GetUserId(), GetUserName()), ct);

        if (result.Outcome == SupplierCommandOutcome.Invalid)
            return BadRequest(new { error = result.Error });

        if (result.Outcome == SupplierCommandOutcome.NameTaken)
            return Conflict(new { code = result.ConflictCode, message = result.ConflictMessage });

        return Ok(result.Supplier);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        var result = await _update.HandleAsync(
            new UpdateSupplierCommand(id, request, GetUserId(), GetUserName()), ct);

        if (result.Outcome == SupplierCommandOutcome.Invalid)
            return BadRequest(new { error = result.Error });

        if (result.Outcome == SupplierCommandOutcome.NameTaken)
            return Conflict(new { code = result.ConflictCode, message = result.ConflictMessage });

        if (result.Outcome == SupplierCommandOutcome.NotFound)
            return NotFound();

        return Ok(result.Supplier);
    }

    /// <summary>
    /// Deactivate rather than delete. Past vouchers and exchange rows point at this supplier, and that
    /// audit trail has to keep naming a real counterparty — a hard delete would either be blocked by the
    /// FK or silently orphan the history.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct = default)
    {
        var result = await _deactivate.HandleAsync(
            new DeactivateSupplierCommand(id, GetUserId(), GetUserName()), ct);

        return result.Outcome == SupplierCommandOutcome.NotFound ? NotFound() : NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<ActionResult<SupplierDto>> Reactivate(Guid id, CancellationToken ct = default)
    {
        var result = await _reactivate.HandleAsync(
            new ReactivateSupplierCommand(id, GetUserId(), GetUserName()), ct);

        if (result.Outcome == SupplierCommandOutcome.NotFound)
            return NotFound();

        return Ok(result.Supplier);
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }

    private string? GetUserName()
        => User.FindFirst("first_name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value;
}
