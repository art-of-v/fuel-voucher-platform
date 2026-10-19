namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The row this type used to be: one (brand, fuel, pool) and the price it sold at.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type exists only so the migration history compiles.</b> <c>fuel_offers</c> was dropped
/// on 2026-10-19; every per-litre figure it carried lives in <see cref="FuelTypeEntity"/> and the
/// sizes a fuel is sold in live in <see cref="FuelNominal"/>, keyed by the pool itself. Nothing in
/// the application maps, queries or writes this type - there is no <c>DbSet</c>, no entity
/// configuration, and no navigation pointing at it.
/// </para>
///
/// <para>
/// It is not deleted because the migration history is append-only. <c>SeedData.Catalogue</c> still
/// builds a set of these for <c>20261015151014_SeedCatalogueAsOffers</c>, and editing a deployed
/// migration to match a later change is how a database that has already applied it stops replaying
/// correctly. The rule that history is never rewritten outranks the tidiness of removing a type
/// nothing calls.
/// </para>
///
/// <para>
/// When a migration predating this one is no longer worth compiling against in a future cleanup, the
/// honest way is a new migration plus a deliberate decision about the old files - not a quiet edit.
/// </para>
/// </remarks>
public sealed class FuelOffer
{
    public string Id { get; set; } = null!;


    public string FuelTypeId { get; set; } = null!;

    /// <summary>Kept only because the historical seed sets it; nothing reads it back.</summary>
    public decimal? SupplierPricePerLiter { get; set; }


    public decimal? PumpPricePerLiter { get; set; }

    public decimal? MinDiscountPerLiter { get; set; }

    /// <summary>
    /// </summary>
    public decimal? FinalPerLiterWith(decimal? marginUahPerLiter) =>
        FuelPricing.FinalPerLiterOrNull(
            SupplierPricePerLiter, marginUahPerLiter, PumpPricePerLiter, MinDiscountPerLiter);

    /// <summary>
    /// Which pool this offer belonged to. It was part of the natural key, not an attribute - the QA
    /// mirror of a live offer was a separate row - which is exactly why the same thing had to be said
    /// about prices: two pools could quote two numbers for one grade.
    /// </summary>
    public string DataKind { get; set; } = DataKinds.Real;

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
