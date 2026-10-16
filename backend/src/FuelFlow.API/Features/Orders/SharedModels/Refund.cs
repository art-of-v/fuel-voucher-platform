namespace FuelFlow.Features.Orders.SharedModels;

public sealed class Refund
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    /// <param name="AmountKopecks">
    /// Kopecks, matching <c>monobank_statement_entries.amount_kopecks</c> and for the same reason: the
    /// acquirer's API speaks kopecks, so this is a boundary value. <c>numeric(14,0)</c> - exact, and
    /// decimal because an integer type holding money is what goes wrong at scale.
    /// </summary>
    /// <remarks>
    /// The column was called <c>amount</c> while holding kopecks. A column whose name omits its unit, in
    /// a schema that also stores UAH, is a 100x bug waiting for someone to read it in the wrong place.
    /// </remarks>
    public decimal AmountKopecks { get; set; }
    public string InvoiceId { get; set; } = null!;
    public string ExtRef { get; set; } = null!;
    public RefundStatus Status { get; set; }
    public string? MonobankStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public bool IsAutomatic { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Order Order { get; set; } = null!;
}

public enum RefundStatus
{
    Processing,
    Completed,
    Failed
}
