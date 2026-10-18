using System.Globalization;

namespace FuelFlow.Features.Brands;

/// <summary>
/// Shared coordinate rules for Brand-node writes (single create/update and bulk import).
/// A price radar is useless without a real position, so every written node MUST carry a
/// lat/lng inside valid WGS84 bounds — the same guard runs on the manual form and the importer.
/// </summary>
public static class BrandStationCoordinates
{
    public static bool IsValidLat(double lat) => lat is >= -90 and <= 90;

    public static bool IsValidLng(double lng) => lng is >= -180 and <= 180;

    /// <summary>
    /// Deterministic external id for an imported node that carries no explicit id, so re-importing the
    /// same brand file updates the same rows instead of duplicating them. Coordinates are the
    /// natural key of a physical АЗК; rounded to 5 dp (~1 m) and rendered invariant.
    /// </summary>
    /// <param name="brandSlug">
    /// The network's slug, <em>not</em> its id. Seeding this with the slug keeps every external id
    /// this derives byte-identical to the ones the text-keyed era wrote, so the migration is a
    /// rename rather than a rewrite.
    /// </param>
    public static string DeriveId(string brandSlug, double lat, double lng) =>
        string.Create(CultureInfo.InvariantCulture, $"{brandSlug}-{lat:F5}-{lng:F5}");
}
