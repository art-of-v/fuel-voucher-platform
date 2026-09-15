namespace FuelFlow.Features.Orders.DeleteMyOrder;

public sealed record DeleteMyOrderCommand(Guid OrderId, Guid UserId);
