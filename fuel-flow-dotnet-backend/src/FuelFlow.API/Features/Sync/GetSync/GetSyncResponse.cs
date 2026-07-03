using FuelFlow.SharedKernel.DTOs;

namespace FuelFlow.Features.Sync.GetSync;

public sealed record GetSyncResponse(
    List<PurchaseDto> Orders,
    int TotalOrders,
    int TotalVouchers
);
