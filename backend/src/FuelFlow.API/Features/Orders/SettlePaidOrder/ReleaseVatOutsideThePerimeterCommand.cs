namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Take back the VAT that the receipt of this order carved out of the part of it which was delivered
/// from stock bought outside the tax perimeter.
/// </summary>
/// <remarks>
/// The command is called by the fulfilment that hands the fuel over, not by the money that paid for
/// it, because the route the fuel came in on is not known until the stock is actually picked. See
/// <see cref="ReleaseVatOutsideThePerimeterCommandHandler"/>.
/// </remarks>
public sealed record ReleaseVatOutsideThePerimeterCommand
{
    /// <summary>The order whose delivery just completed.</summary>
    public required Guid OrderId { get; init; }
}
