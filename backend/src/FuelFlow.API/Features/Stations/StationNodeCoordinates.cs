using System.Globalization;

namespace FuelFlow.Features.Stations;

/// <summary>
/// Shared coordinate rules for station-node writes (single create/update and bulk import).
/// A price radar is useless without a real position, so every written node MUST carry a
/// lat/lng inside valid WGS84 bounds — the same guard runs on the manual form and the importer.
/// </summary>
public static class StationNodeCoordinates
{
    public static bool IsValidLat(double lat) => lat is >= -90 and <= 90;

    public static bool IsValidLng(double lng) => lng is >= -180 and <= 180;

    /// <summary>
    /// Deterministic id for an imported node that carries no explicit id, so re-importing the
    /// same brand file updates the same rows instead of duplicating them. Coordinates are the
    /// natural key of a physical АЗК; rounded to 5 dp (~1 m) and rendered invariant.
    /// </summary>
    public static string DeriveId(string stationId, double lat, double lng) =>
        string.Create(CultureInfo.InvariantCulture, $"{stationId}-{lat:F5}-{lng:F5}");
}
