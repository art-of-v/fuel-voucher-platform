namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// Whether the brand printed on a voucher agrees with the brand the fuel it claims belongs to.
/// </summary>
/// <remarks>
/// <c>provider</c> and <c>fuel_types.station_id</c> are the same fact written twice — the brand on the
/// paper and the brand in the catalog — and only one of them is constrained. A voucher whose two copies
/// disagree is invisible in practice rather than visibly wrong: renewal matching compares both fields
/// together, so it finds no replacement, and fuel-type pricing is keyed by the catalog brand, so nothing
/// prices it correctly. It just stops being sellable with no error anywhere.
/// <para>
/// The comparison is case-insensitive and whitespace-tolerant because the brand is read off paper in caps
/// ("OKKO") while the catalog stores it lower ("okko") — every other provider comparison in the codebase
/// already folds case for that reason.
/// </para>
/// </remarks>
public static class BrandFuelPairing
{
    /// <param name="parsedProvider">The brand as read off the voucher's paper.</param>
    /// <param name="catalogBrand">
    /// The fuel type's <c>station_id</c>, or <c>null</c> when the fuel type has no brand recorded — in
    /// which case there is nothing to contradict and the voucher is judged on its own merits.
    /// </param>
    public static bool IsConsistent(string? parsedProvider, string? catalogBrand)
    {
        if (catalogBrand is null)
        {
            return true;
        }

        var printed = parsedProvider?.Trim();
        if (string.IsNullOrEmpty(printed))
        {
            // Nothing was read off the paper, so there is no disagreement to report; the absence of a
            // brand is already covered by the parser's own confidence rules.
            return true;
        }

        return string.Equals(printed, catalogBrand.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}