using FuelFlow.Features.Orders.DeleteOrder;
using FuelFlow.Features.Orders.GetAdminOrderById;
using FuelFlow.Features.Orders.GetAdminOrders;
using FuelFlow.Features.Orders.UpdateOrderStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Orders;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public sealed class AdminOrderController : ControllerBase
{
    private readonly GetAdminOrdersQueryHandler _getAllHandler;
    private readonly GetAdminOrderByIdQueryHandler _getByIdHandler;
    private readonly UpdateOrderStatusCommandHandler _updateHandler;
    private readonly DeleteOrderCommandHandler _deleteHandler;

    public AdminOrderController(
        GetAdminOrdersQueryHandler getAllHandler,
        GetAdminOrderByIdQueryHandler getByIdHandler,
        UpdateOrderStatusCommandHandler updateHandler,
        DeleteOrderCommandHandler deleteHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _getAllHandler.HandleAsync(new GetAdminOrdersQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _getByIdHandler.HandleAsync(new GetAdminOrderByIdQuery(id), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateOrderRequest request, CancellationToken cancellationToken)
    {
        var success = await _updateHandler.HandleAsync(new UpdateOrderStatusCommand(id, request.Status), cancellationToken);
        if (!success) return NotFound();
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var success = await _deleteHandler.HandleAsync(new DeleteOrderCommand(id), cancellationToken);
        if (!success) return NotFound();
        return Ok(new { success = true });
    }
}

public sealed class UpdateOrderRequest
{
    public string? Status { get; set; }
}
