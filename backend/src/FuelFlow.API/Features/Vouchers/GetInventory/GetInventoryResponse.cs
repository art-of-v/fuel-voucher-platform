namespace FuelFlow.Features.Vouchers.GetInventory;

public sealed record InventoryItemDto(
    string Brand,
    Guid FuelTypeId,
    string FuelTypeName,
    decimal Liters,
    int Available,
    int Assigned,
    int PendingReconciliation,
    int Used,
    int Expired,
    int Total
);

public sealed record GetInventoryResponse(List<InventoryItemDto> Inventory);
