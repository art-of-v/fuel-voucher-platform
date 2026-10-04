namespace FuelFlow.Features.Orders.SharedModels;

public class OrderLineItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>
    /// Pump-based list total for this line (UAH), frozen at checkout via
    /// <see cref="FuelFlow.SharedKernel.Domain.FuelPricing.OriginalPackagePrice"/>. The customer's
    /// saving vs the pump is <c>OriginalLineTotal - LineTotal</c>. Null for rows predating this
    /// column (no backfill — historical pump price cannot be reconstructed) and for lines with no
    /// pump reference (e.g. voucher renewals), which therefore contribute no saving.
    /// </summary>
    public decimal? OriginalLineTotal { get; set; }

    /// <summary>
    /// The term the customer bought this fuel for (<c>VoucherRenewalTerm.Code()</c>), frozen at
    /// checkout. Null means the full remaining supplier term was sold, which is the behaviour for every
    /// order placed before short terms existed.
    /// </summary>
    public string? TermCode { get; set; }

    public Order Order { get; set; } = null!;
}
