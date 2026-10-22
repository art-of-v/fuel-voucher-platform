using FuelFlow.SharedKernel.Errors;
namespace FuelFlow.Features.Orders.UpdateOrderStatus;

public sealed record UpdateOrderStatusCommand(Guid Id, string? Status);

/// <summary>
/// The answer to a status change: was the order found, was the change made, and — when it was
/// not — why not. The reason used to be an empty 403 on the hierarchy and a bare "false" here,
/// so the operator saw "failed to change the order's status" with nothing to act on.
/// </summary>
public sealed record UpdateOrderStatusResult(bool Found, bool Success, string? Error)
{
    public static UpdateOrderStatusResult NotFound() => new(false, false, "Order not found");

    public static UpdateOrderStatusResult Refused(string error) => new(true, false, error);

    public static UpdateOrderStatusResult Done() => new(true, true, null);
}
