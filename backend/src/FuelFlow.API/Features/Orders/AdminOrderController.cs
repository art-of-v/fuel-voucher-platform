using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.DeleteOrder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FuelFlow.Features.Orders;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Policy = "Staff")]
public sealed class AdminOrderController : ControllerBase
{
    private readonly DeleteOrderCommandHandler _deleteHandler;
    private readonly RefundOrderCommandHandler _refundHandler;

    public AdminOrderController(
        DeleteOrderCommandHandler deleteHandler,
        RefundOrderCommandHandler refundHandler)
    {
        _deleteHandler = deleteHandler;
        _refundHandler = refundHandler;
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

public sealed class RefundOrderRequest
{
    public long? AmountKopecks { get; set; }
    public string? Reason { get; set; }
}
