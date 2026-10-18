namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The volume row this type used to be: a nominal scoped to one offer's pool.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type exists only so the migration history compiles.</b> `fuel_offer_volumes` was dropped
/// on 2026-10-18; the sizes a fuel is sold in live in <see cref="FuelNominal"/> and belong to the
/// fuel rather than to a pool. Nothing in the application maps, queries or writes this type - there
/// is no <c>DbSet</c>, no entity configuration, and no navigation pointing at it.
/// </para>
///
/// <para>
/// It is not deleted because the migration history is append-only. <c>20261015151014_SeedCatalogueAsOffers</c>
/// declares a private helper that takes one of these to compute the seed's package price, and editing
/// a deployed migration to match a later change is how a database that has already applied it stops
/// replaying correctly. The rule that history is never rewritten outranks the tidiness of removing a
/// type nothing calls.
/// </para>
///
/// <para>
/// When a migration predating this one is no longer worth compiling against in a future cleanup, the
/// honest way is a new migration plus a deliberate decision about the old files - not a quiet edit.
/// </para>
/// </remarks>
public sealed class FuelOfferVolume
{
    public string Id { get; set; } = null!;

    public string OfferId { get; set; } = null!;

    public decimal Liters { get; set; }

    public decimal? PriceOverrideUah { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}