namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A voucher import grouped per fuel type — партія = (ImportJobId × FuelTypeId). Carries who supplied it
/// and nothing else numeric: the cost now lives on each voucher (<c>FuelVoucher.CostPerLiter</c>), because
/// vouchers of the same brand do not cost the same and one customer payment reduces one voucher's cost only.
/// </summary>
/// <remarks>
/// A row exists <b>only once the cost has been entered</b> — the absence of a batch row for an
/// (ImportJobId, FuelTypeId) pair means "no cost yet", which the activation gate refuses to sell.
/// <see cref="Provider"/> is the brand, denormalized for reporting; <see cref="SupplierId"/> is the company
/// the stock was actually bought from.
/// </remarks>
public sealed class PurchaseBatch
{
    public Guid Id { get; set; }
    public Guid ImportJobId { get; set; }
    public string FuelTypeId { get; set; } = null!;

    /// <summary>Supplier station id (e.g. "okko"), denormalized from the batch's vouchers for reporting.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// The company this stock was bought from. Unlike <see cref="Provider"/> (the brand) this is the party we
    /// settle with and hand vouchers back to.
    /// </summary>
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public Guid? EnteredByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
