namespace FuelFlow.Features.Orders.SharedModels;

public class OrderLineItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Brand { get; set; } = null!;
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

    /// <summary>
    /// The inputs that produced <see cref="UnitPrice"/>, frozen at checkout, as a JSON object:
    /// the per-litre cost, the margin, the pump price, the minimum discount and the terms id this
    /// line was priced under.
    /// </summary>
    /// <remarks>
    /// <see cref="UnitPrice"/> records what the customer paid; this records <em>why</em>. Without it
    /// the answer to "why was this order 89.06?" is only available at the moment of checkout, and
    /// after a contract changes it is gone — the live contract is not the one that was in force when
    /// the order was placed, so it cannot be used to reconstruct the basis.
    /// <para>
    /// <b>Null for every order placed before this column existed, and deliberately not backfilled.</b>
    /// Backfilling from today's contract or today's fuel configuration would manufacture a history
    /// that never happened, which is worse than an honest gap: it looks like evidence.
    /// </para>
    /// </remarks>
    public string? PricingBasis { get; set; }

    public Order Order { get; set; } = null!;
}
