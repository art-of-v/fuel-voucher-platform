using FuelFlow.SharedKernel.DTOs;

namespace FuelFlow.Features.Orders.SimulatePayment;

public sealed class SimulatePaymentResponse
{
    public string Status { get; set; } = null!;
    public PurchaseDto? Purchase { get; set; }
}
