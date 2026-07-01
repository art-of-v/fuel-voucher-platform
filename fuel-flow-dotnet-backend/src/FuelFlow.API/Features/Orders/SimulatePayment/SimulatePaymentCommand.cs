namespace FuelFlow.Features.Orders.SimulatePayment;

public sealed class SimulatePaymentCommand
{
    public Guid OrderId { get; set; }
    public string Scenario { get; set; } = "success";
}
