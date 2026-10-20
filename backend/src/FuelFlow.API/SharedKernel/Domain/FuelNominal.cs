namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A size, in litres, that this fuel is sold in - 5, 10, 20, 50 - in one pool.
/// </summary>
/// <para>
/// <b>A nominal belongs to the fuel and the pool, not to an offer.</b> It used to live on
/// <c>fuel_offer_volumes</c>, which meant one row per (brand x fuel x pool x size): the same 10 litres
/// written four times and four places to keep in step. The pool is what remains of that, because the
/// rehearsal catalogue may legitimately sell sizes the live one does not - a 7 L canister that exists
/// only to rehearse a pickup is not a size the public site should list.
/// </para>
/// <para>
/// <b>What does not remain is the price.</b> There is no per-size price anywhere: a nominal sells at
/// its fuel's rate, capped by the ceiling on <see cref="FuelTypeEntity"/>. That is why the pool can
/// stay while the offer goes - the offer carried one thing a pool genuinely needed (the size list)
/// and four the fuel does not need to be told twice (cost, ceiling, guaranteed discount, margin
/// percent).
/// </para>
/// <para>
/// <b>A row here means the operator has chosen to sell this size in this pool.</b> It does not mean
/// the fuel is in stock: a voucher of 5 litres imported before the operator adds 5 to the nominal
/// list exists, pays for, and is deliberately invisible on the shelf until that happens. Availability
/// is a separate question and <c>PackageStockAvailability</c> answers it from the vouchers.
/// </para>
/// <para>
/// Keyed by (fuel, pool, litres) rather than by a surrogate id. A nominal has no life of its own - it
/// is the intersection of a fuel, a pool and a number - and a synthetic key here would be one more
/// column to carry through every join and one more thing that could be wrong while the triple it
/// stands for stayed right.
/// </para>
/// </remarks>
public sealed class FuelNominal
{
    public Guid FuelTypeId { get; set; }

    /// <summary>
    /// <c>live</c> or <c>qa</c>, from <see cref="DataKinds"/>. A named value rather than a boolean, so
    /// a row cannot claim a provenance that does not exist.
    /// </summary>
    public string DataKind { get; set; } = DataKinds.Real;

    public decimal Liters { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}