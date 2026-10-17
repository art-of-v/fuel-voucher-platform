namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Builds the key of a <see cref="FuelOffer"/> from the pair that identifies it within a pool.
/// </summary>
/// <remarks>
/// <para>
/// The offer's identity is <c>(station_id, fuel_type_id, data_kind)</c>, not any one of the three.
/// Dropping the pool would merge the QA mirror of a live offer into the live offer, which is the one
/// thing the storefront filter must never be able to do; dropping the station would make a fuel that
/// two networks list collide.
/// </para>
/// <para>
/// <b>The id is <c>fuel_type_id</c> alone</b>, with <c>-qa</c> appended for the rehearsal pool. The
/// station is part of the offer's identity and stays a column of its own, but it is not part of the
/// key: it mirrors <c>fuel_types.station_id</c>, and a fuel type belongs to exactly one station, so
/// the fuel id already determines the station. Spelling the station out produced
/// <c>okko-okko-95</c> — the brand twice, because a seeded fuel id already starts with it — and
/// <c>okko-3808d410-b9ae-4511-9dfc-d6564fa2e1ad</c> for a fuel whose id is a UUID, so the same column
/// carried two shapes depending on which write path created the row.
/// </para>
/// <para>
/// Uniqueness therefore rests on <c>fuel_type_id</c> being the primary key of <c>fuel_types</c>. That
/// is what <c>deploy/preflight-db/012</c> asks before every pull: an offer whose station disagrees
/// with its fuel type's station would break the key's premise, and the database cannot catch it across
/// tables.
/// </para>
/// <para>
/// <b>The rule is stated a third time in SQL</b>, because a migration cannot call into the assembly it
/// migrates from: <c>DropTheStationPrefixFromOfferKeys</c> re-keys the rows the historical
/// <c>SplitFuelPackagesIntoOffers</c> backfill wrote under the old rule. That migration is history and
/// keeps its old expression; <c>OfferBackfillIntegrationTests</c> asserts the ids a fully migrated
/// database holds are the ones this method computes.
/// </para>
/// <para>
/// The result is bounded by the fuel id plus the suffix, hence the 103-character CHECK on the column
/// rather than the 100 the single-column keys use.
/// </para>
/// </remarks>
public static class OfferKeys
{
    /// <summary>
    /// Suffix that distinguishes a rehearsal offer from the live one it mirrors.
    /// </summary>
    private const string QaSuffix = "qa";

    /// <summary>
    /// The key for one offer. The inverse of the group the backfill forms, so
    /// <c>OfferKeys.For(fuel, pool)</c> is stable and total.
    /// </summary>
    /// <param name="fuelTypeId">
    /// Fuel id, see <see cref="FuelOffer.FuelTypeId"/>. Its station is
    /// <see cref="FuelOffer.StationId"/>, which it mirrors, and which is therefore not needed here.
    /// </param>
    /// <param name="dataKind"><see cref="DataKinds.Real"/> or <see cref="DataKinds.Qa"/>.</param>
    public static string For(string fuelTypeId, string dataKind)
    {
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

        return dataKind == DataKinds.Qa ? $"{fuelTypeId}-{QaSuffix}" : fuelTypeId;
    }
}