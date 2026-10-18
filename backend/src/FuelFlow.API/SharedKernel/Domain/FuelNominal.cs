namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A size, in litres, that this fuel is sold in - 5, 10, 20, 50.
/// <para>
/// <b>A nominal belongs to the fuel, not to an offer.</b> It used to live on <c>fuel_offer_volumes</c>,
/// which meant one row per (brand × fuel × pool × size): the same 10 litres written four times and four
/// places to keep in step. The sizes a brand sells are the same in both pools and the same whatever the
/// cost, so they are one fact about the grade and this is where it lives.
/// </para>
/// <para>
/// <b>A row here means the operator has chosen to sell this size.</b> It does not mean the fuel is in
/// stock: a voucher of 5 litres imported before the operator adds 5 to the nominal list exists, pays for,
/// and is deliberately invisible on the shelf until that happens. Availability is a separate question and
/// <see cref="PackageStockAvailability"/> answers it from the vouchers.
/// </para>
/// <para>
/// Keyed by (fuel, litres) rather than by a surrogate id. A nominal has no life of its own - it is the
/// intersection of a fuel and a number - and a synthetic key here would be one more column to carry
/// through every join and one more thing that could be wrong while the pair it stands for stayed right.
/// </para>
/// </summary>
public sealed class FuelNominal
{
    public string FuelTypeId { get; set; } = null!;

    public decimal Liters { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}