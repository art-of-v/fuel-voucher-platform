namespace FuelFlow.SharedKernel.Domain;

public sealed class Brand
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Color { get; set; } = "#00ff80";
    public string LogoText { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? StationType { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }

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
}
