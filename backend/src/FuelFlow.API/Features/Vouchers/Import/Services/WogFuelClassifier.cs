using System.Text.RegularExpressions;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// Canonical, drift-tolerant fuel categories for WOG vouchers.
/// <para>
/// A WOG fuel product has no stable numeric code in its QR payload (the payload is just the
/// voucher number), so the only fuel signal is the voucher's OCR text. Matching that text
/// against the localized <c>fuel_types.name</c> by string equality is brittle: the display
/// string varies by data entry (Latin "A-95" vs Cyrillic "А-95", hyphen/space, "EURO"/"ЄВРО"
/// or "Mustang" suffix). That brittleness is the same class of bug that rejected every OKKO
/// A-95 voucher in production, and here it did worse — the old parser fell back to a hardcoded
/// <c>"wog-dp"</c> id, which both mislabels the voucher as diesel and, when that id is absent
/// in a drifted production catalog, trips the <c>fuel_type_id</c> foreign key and 500s the
/// whole import. Resolution maps both the voucher text and each DB display name onto this
/// shared category space so a match survives display-name drift, and returns null (never a
/// guessed default) when no category matches.
/// </para>
/// </summary>
public enum WogFuelCategory
{
    Diesel,        // catalog: "ДП Mustang"
    Petrol95,      // catalog: "A-95 Mustang" — premium 95
    Petrol95Euro,  // catalog: "A 95 EURO"   — standard 95
    Petrol100,     // catalog: "Mustang 100"
    Gas            // catalog: "ГАЗ"
}

/// <summary>
/// Resolves WOG fuel types from raw OCR voucher text, independent of the localized fuel-type
/// display name. Text is the only signal available for WOG (its QR carries no product code).
/// </summary>
public static class WogFuelClassifier
{
    // Petrol "95" in raw OCR text must sit next to an A/А marker so a bare "95" inside a voucher
    // number or a date cannot be misread as a fuel grade. Latin "a" and Cyrillic "а" are both
    // accepted, with an optional hyphen/space between the letter and the digits.
    private static readonly Regex Petrol95InText =
        new(@"[аa]\s*[-–—]?\s*95", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // WOG's only 98/100-octane grade is branded "Mustang 100". Match that phrase (Latin or
    // Cyrillic "Mustang", any whitespace incl. a line break between the word and the number) or
    // an A-anchored 98/100 octane token. Anchoring keeps a "100 л" volume from reading as a grade.
    private static readonly Regex Petrol100InText =
        new(@"(?:mustang|мустанг)\s*100|[аa]\s*[-–—]?\s*(?:98|100)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "ДП" (diesel) as a token, tolerating a space and a Latin "P" for the Cyrillic "П" (a common
    // OCR confusion). .NET \b honours Unicode word chars, so this matches the token in "ДП Mustang"
    // without matching larger words.
    private static readonly Regex DieselToken =
        new(@"\bд\s*[пp]\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Classifies a DB fuel-type display name (clean text) into a canonical category.</summary>
    public static WogFuelCategory? CategoryFromName(string? name) => Classify(name, strict95: false);

    /// <summary>Classifies raw OCR voucher text into a canonical category (95 must be anchored to an A marker).</summary>
    public static WogFuelCategory? CategoryFromText(string? text) => Classify(text, strict95: true);

    private static WogFuelCategory? Classify(string? source, bool strict95)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var lower = source.ToLowerInvariant();

        if (lower.Contains("газ") || lower.Contains("gas") || lower.Contains("лпг") || lower.Contains("lpg"))
            return WogFuelCategory.Gas;

        // Diesel is checked before the Mustang marker: the diesel catalog row is "ДП Mustang",
        // so a Mustang-branded diesel must resolve to Diesel, not to a premium petrol grade.
        if (DieselToken.IsMatch(lower) || lower.Contains("дизель") || lower.Contains("diesel"))
            return WogFuelCategory.Diesel;

        // "Mustang 100" is checked before 95 so it is never mistaken for a petrol-95 grade. It
        // carries no "95" token, so the two never overlap, but the ordering keeps intent explicit.
        if (Petrol100InText.IsMatch(lower))
            return WogFuelCategory.Petrol100;

        var has95 = strict95 ? Petrol95InText.IsMatch(lower) : lower.Contains("95");
        if (has95)
        {
            var isMustang = lower.Contains("mustang") || lower.Contains("мустанг");
            return isMustang ? WogFuelCategory.Petrol95 : WogFuelCategory.Petrol95Euro;
        }

        return null;
    }

    /// <summary>
    /// Resolves the WOG fuel type for a parsed voucher from its OCR text.
    /// <para>
    /// Returns null (rather than guessing a default) when the text yields no category, or when
    /// the resolved category has no matching row in the station catalog, so the caller rejects
    /// the row with a clear message instead of silently importing it under the wrong fuel type
    /// or, worse, under a non-existent id that would trip the foreign key when the voucher
    /// row is persisted.
    /// </para>
    /// </summary>
    public static FuelTypeEntity? ResolveFuelType(
        string? rawText,
        IReadOnlyCollection<FuelTypeEntity> wogFuelTypes)
    {
        var category = CategoryFromText(rawText);
        if (category is null)
            return null;

        return wogFuelTypes.FirstOrDefault(ft => CategoryFromName(ft.Name) == category);
    }
}
