namespace FuelFlow.Features.Orders.UpdateOrderStatus;

public sealed record UpdateOrderStatusCommand(Guid Id, string? Status);
