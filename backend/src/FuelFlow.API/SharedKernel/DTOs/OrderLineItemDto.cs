namespace FuelFlow.SharedKernel.DTOs;

public sealed class OrderLineItemDto
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>
    /// Transitional alias for <see cref="Brand"/>, serialised under the field's old name. The
    /// published 1.0.1 app reads <c>provider</c> here; see <see cref="VoucherDto.Provider"/> for why
    /// both ship for one release and when this goes.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("provider")]
    public string Provider => Brand;
}
