using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.DeleteOrder;
using FuelFlow.Features.Orders.GetAdminOrderById;
using FuelFlow.Features.Orders.GetAdminOrders;
using FuelFlow.Features.Orders.UpdateOrderStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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
    private readonly RefundOrderCommandHandler _refundHandler;

    public AdminOrderController(
        GetAdminOrdersQueryHandler getAllHandler,
        GetAdminOrderByIdQueryHandler getByIdHandler,
        UpdateOrderStatusCommandHandler updateHandler,
        DeleteOrderCommandHandler deleteHandler,
        RefundOrderCommandHandler refundHandler)
    {
        _getAllHandler = getAllHandler;
        _getByIdHandler = getByIdHandler;
        _updateHandler = updateHandler;
        _deleteHandler = deleteHandler;
        _refundHandler = refundHandler;
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

    [HttpPost("{id}/refund")]
    public async Task<IActionResult> Refund([FromRoute] Guid id, [FromBody] RefundOrderRequest? request, CancellationToken cancellationToken)
    {
        var command = new RefundOrderCommand
        {
            OrderId = id,
            AmountKopecks = request?.AmountKopecks,
            Reason = request?.Reason,
            ChangedByUserId = GetUserId(),
            ChangedByUserName = GetUserName(),
            IsAutomatic = false
        };

        var result = await _refundHandler.HandleAsync(command, cancellationToken);

        return result.Status switch
        {
            "NotFound" => NotFound(new { success = false, error = result.ErrorMessage }),
            "NotPayable" => BadRequest(new { success = false, error = result.ErrorMessage }),
            "NothingToRefund" => BadRequest(new { success = false, error = result.ErrorMessage }),
            "Failed" => StatusCode(StatusCodes.Status502BadGateway, new { success = false, error = result.ErrorMessage }),
            _ => Ok(new
            {
                success = true,
                refundId = result.RefundId,
                orderId = result.OrderId,
                amountKopecks = result.AmountKopecks,
                status = result.Status
            })
        };
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
        {
            return $"{first} {last}".Trim();
        }
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}

public sealed class UpdateOrderRequest
{
    public string? Status { get; set; }
}

public sealed class RefundOrderRequest
{
    public int? AmountKopecks { get; set; }
    public string? Reason { get; set; }
}
