using FuelFlow.Features.Purchases.DeletePurchase;
using FuelFlow.Features.Purchases.GetAdminPurchaseById;
using FuelFlow.Features.Purchases.GetAdminPurchases;
using FuelFlow.Features.Purchases.UpdatePurchase;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Purchases;

[ApiController]
[Route("api/admin/purchases")]
[Authorize(Roles = "Admin")]
public sealed class AdminPurchaseController : ControllerBase
{
    private readonly GetAdminPurchasesQueryHandler _getAll;
    private readonly GetAdminPurchaseByIdQueryHandler _getById;
    private readonly UpdatePurchaseCommandHandler _update;
    private readonly DeletePurchaseCommandHandler _delete;

    public AdminPurchaseController(
        GetAdminPurchasesQueryHandler getAll,
        GetAdminPurchaseByIdQueryHandler getById,
        UpdatePurchaseCommandHandler update,
        DeletePurchaseCommandHandler delete)
    {
        _getAll = getAll;
        _getById = getById;
        _update = update;
        _delete = delete;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetAdminPurchasesQuery(), ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetAdminPurchaseByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UpdatePurchaseRequest request, CancellationToken ct)
    {
        var success = await _update.HandleAsync(new UpdatePurchaseCommand(id, request.Status), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken ct)
    {
        var success = await _delete.HandleAsync(new DeletePurchaseCommand(id), ct);
        return success ? Ok(new { success = true }) : NotFound();
    }
}

public sealed class UpdatePurchaseRequest
{
    public string? Status { get; set; }
}
