namespace FuelFlow.Features.Brands;

/// <summary>
/// The stable codes a station-node write is refused under, so the admin can say the reason in the
/// operator's language instead of printing the English sentence.
/// </summary>
internal static class BrandStationRefusalCodes
{
    public const string BrandAndNameRequired = "station_brand_and_name_required";

    public const string CoordinatesInvalid = "station_coordinates_invalid";

    public const string BrandMissing = "station_brand_missing";

    public const string ExternalIdConflict = "station_external_id_conflict";

    public const string NotFound = "station_not_found";
}
