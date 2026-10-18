namespace FuelFlow.SharedKernel.Domain;

public sealed class Brand
{
    /// <summary>
    /// Surrogate key. Nothing outside the database has to know it, and nothing derives meaning from it.
    /// </summary>
    /// <remarks>
    /// This was the network's own code (<c>"okko"</c>), which made the primary key a magic string that
    /// every write path, every import and every test had to spell correctly. Planning D-21: the
    /// natural key moved to <see cref="Slug"/> and the identity became opaque.
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>
    /// The network's own code — <c>"okko"</c>, <c>"wog"</c>, <c>"klo"</c> — and the one name a supplier,
    /// an invoice or a parser can be asked for. Unique, and preserved across the whole migration off
    /// the natural key, so a rollback loses nothing.
    /// </summary>
    public string Slug { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string Color { get; set; } = "#00ff80";
    public string LogoText { get; set; } = null!;

    /// <summary>
    /// Display priority in the mobile app's Brand list. Lower = higher.
    /// Defaults to 999 ("no explicit priority") so providers created without
    /// an explicit order sort after the seeded legacy brands (1..4).
    /// </summary>

    public int SortOrder { get; set; } = 999;

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

    /// <summary>
    /// The physical locations belonging to this network. The inverse of
    /// <see cref="BrandStation.Brand"/>, declared here rather than only as a foreign key so EF has one
    /// relationship to model instead of two configurations of the same one.
    /// </summary>
    public ICollection<BrandStation> Stations { get; set; } = new List<BrandStation>();
}
