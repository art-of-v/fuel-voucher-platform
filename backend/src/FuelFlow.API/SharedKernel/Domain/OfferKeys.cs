namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Builds the natural key of a <see cref="FuelOffer"/> from the triple that identifies it.
/// </summary>
/// <remarks>
/// <para>
/// The offer's identity is <c>(station_id, fuel_type_id, data_kind)</c>, not any one of the three.
/// Dropping the pool would merge the QA mirror of a live offer into the live offer, which is the one
/// thing the storefront filter must never be able to do; dropping the station would make a fuel that
/// two networks list collide.
/// </para>
/// <para>
/// The id is <c>station_id + "-" + fuel_type_id</c>, with <c>-qa</c> appended for the rehearsal pool.
/// <b>That is not the same string as the SQL.</b> The migration computes it inline, because a
/// migration cannot call into the assembly it is migrating from — so this method and the
/// <c>INSERT ... SELECT</c> in <c>SplitFuelPackagesIntoOffers</c> are two statements of one rule, and
/// <c>FuelOfferKeyTests</c> asserts the migration's output equals this method's output for every row.
/// </para>
/// <para>
/// The result is bounded by the two component lengths plus the separator, hence the 200-character
/// CHECK on the column rather than the 100 the single-column keys use.
/// </para>
/// </remarks>
public static class OfferKeys
{
    /// <summary>
    /// Suffix that distinguishes a rehearsal offer from the live one it mirrors.
    /// </summary>
    private const string QaSuffix = "qa";

    /// <summary>
    /// The natural key for one offer. The inverse of the group the backfill forms, so
    /// <c>OfferKeys.For(station, fuel, pool)</c> is stable and total.
    /// </summary>
    /// <param name="stationId">Network id, see <see cref="FuelOffer.StationId"/>.</param>
    /// <param name="fuelTypeId">Fuel id, see <see cref="FuelOffer.FuelTypeId"/>.</param>
    /// <param name="dataKind"><see cref="DataKinds.Real"/> or <see cref="DataKinds.Qa"/>.</param>
    public static string For(string stationId, string fuelTypeId, string dataKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fuelTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataKind);

        if (dataKind is not (DataKinds.Real or DataKinds.Qa))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dataKind),
                dataKind,
                $"the offer key is defined for {DataKinds.Real} and {DataKinds.Qa} only; " +
                $"anything else would silently produce a key the database rejects");
        }

        var key = $"{stationId}-{fuelTypeId}";
        return dataKind == DataKinds.Qa ? $"{key}-{QaSuffix}" : key;
    }
}