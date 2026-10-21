namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A voucher import grouped per fuel type — партія = (ImportJobId × FuelTypeId). Carries who supplied it
/// and nothing else numeric: the cost now lives on each voucher (<c>FuelVoucher.CostPerLiter</c>), because
/// vouchers of the same brand do not cost the same and one customer payment reduces one voucher's cost only.
/// </summary>
/// <remarks>
/// A row exists <b>only once the cost has been entered</b> — the absence of a batch row for an
/// (ImportJobId, FuelTypeId) pair means "no cost yet", which the activation gate refuses to sell.
/// <see cref="Brand"/> is the brand, denormalized for reporting; <see cref="SupplierId"/> is the company
/// the stock was actually bought from.
/// </remarks>
public sealed class PurchaseBatch
{
    public Guid Id { get; set; }
    public Guid ImportJobId { get; set; }
    public Guid FuelTypeId { get; set; }

    /// <summary>Supplier Brand id (e.g. "okko"), denormalized from the batch's vouchers for reporting.</summary>
    public string Brand { get; set; } = null!;

    /// <summary>
    /// The brand's id - the key the text above is a copy of.
    /// </summary>
    /// <remarks>
    /// The brand is written twice on this row: as the name the application and the importers speak,
    /// and - from the key - as the id of the brand itself. The name is what a report groups by and
    /// what a parser reads; the id is what the database can check. A composite foreign key over
    /// (fuel type, brand) says the two copies are the same fact; a trigger can only ask.
    /// <para>
    /// The key is required, and the database says so: the composite foreign key
    /// <c>(fuel_type_id, brand_id) → fuel_types(id, brand_id)</c> is what refuses a row whose brand
    /// is not its fuel's brand. A nullable key would be a key the database does not check, because a
    /// foreign key with a NULL in it is not compared at all.
    /// </para>
    /// </remarks>
    public Guid BrandId { get; set; }

    /// <summary>
    /// The company this stock was bought from. Unlike <see cref="Brand"/> (the brand) this is the party we
    /// settle with and hand vouchers back to.
    /// </summary>
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public Guid? EnteredByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
