namespace FuelFlow.SharedKernel.Domain;

public sealed class BrandStation
{
    /// <summary>Surrogate key. Nothing derives meaning from it and nothing outside the database quotes it.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The natural key the importer upserts on, unique per brand: an id the source file supplied, or
    /// the one <see cref="BrandStationCoordinates"/> derives from the coordinates.
    /// </summary>
    /// <remarks>
    /// This was the primary key, which made re-import idempotent for free — the same file produced the
    /// same id and healed the same rows. A surrogate id would have silently turned every re-import
    /// into a duplicate insert of the whole network, so the key moved here rather than disappearing.
    /// </remarks>
    public string ExternalId { get; set; } = null!;

    public Guid BrandId { get; set; }

    /// <summary>The network this АЗК belongs to. See <see cref="ExternalId"/> for why the key is split.</summary>
    public Brand? Brand { get; set; }

    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Archive, not delete. Deleting a catalog entry that fuel has already been sold against would
    /// cascade into the vouchers - real, paid-for paper - and destroy the only record of what a
    /// customer bought (#217). A used entry is soft-deleted: hidden from the catalog, still there for
    /// reports and for every historical row that points at it.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>When the entry was archived, or null while it is live.</summary>
    public DateTime? DeletedAtUtc { get; set; }
}
