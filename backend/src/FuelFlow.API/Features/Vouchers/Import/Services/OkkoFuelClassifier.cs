using System.Text.RegularExpressions;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// Canonical, non-localized fuel categories for OKKO vouchers.
/// <para>
/// The stable identity of an OKKO fuel product is the numeric product code embedded at the
/// start of the QR payload, not the localized <c>fuel_types.name</c> display string. That
/// display string varies by data entry (Latin "A-95" vs Cyrillic "А95", optional hyphen,
/// optional "ЄВРО"/"EURO" suffix), so matching it by string equality is brittle and was the
/// cause of every A-95/ГАЗ/PULLS voucher being rejected in production. Resolution maps both
/// the QR product code and the DB display name onto this shared category space so the QR path
/// and the text path agree and neither depends on an exact display name.
/// </para>
/// </summary>
public enum OkkoFuelCategory
{
    A95,
    A95Pulls,
    Diesel,
    DieselPulls,
    Gas
}

/// <summary>
/// Resolves OKKO fuel types from the stable QR product code (primary) or raw OCR text
/// (fallback), independent of the localized fuel-type display name.
/// </summary>
public static class OkkoFuelClassifier
{
    // OKKO QR payloads open with "<productCode>$": e.g. "9018$2000$;99999...=...?".
    private static readonly Regex ProductCodeRegex = new(@"^(\d+)\$", RegexOptions.Compiled);

    // Petrol "95" in raw OCR text must sit next to an A/А marker so that a bare "95" inside a
    // voucher number or a date cannot be misread as a fuel grade. Latin "a" and Cyrillic "а"
    // are both accepted, with an optional hyphen/space between the letter and the digits.
    private static readonly Regex Petrol95InText =
        new(@"[аa]\s*[-–—]?\s*95", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "ДП" (diesel) as a standalone token. .NET \b honours Unicode word chars, so this
    // matches the Cyrillic token in "ДП ЄВРО" / "ДП PULLS" without matching larger words.
    //
    // The Latin "DP" is the same grade written by the brand's own code and by operators who paste
    // it. Without it a hand-entered "DP" classifies as no grade at all, which is the near-duplicate
    // this whole identity work exists to remove: "ДП" and "DP" are one fuel spelled two ways.
    private static readonly Regex DieselToken =
        new(@"\bдп\b|\bdp\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The one name a grade is sold under in this system, derived from the brand's code rather than
    /// from any label the brand renders.
    /// <para>
    /// The site prints "A-95 ЄВРО", "ДП ЄВРО", "95 PULLS", "100 PULLS" — a label per brand, per
    /// locale, per designer. Those are renderings of a grade, not its identity, and taking one as the
    /// name is what put "95" and "ДП" in the catalogue beside the "100 PULLS" and "ДП PULLS" that
    /// already meant those grades. The code is the identity, so the name follows the code.
    /// </para>
    /// </summary>
    public static string CanonicalName(OkkoFuelCategory category) => category switch
    {
        OkkoFuelCategory.A95 => "А-95",
        OkkoFuelCategory.A95Pulls => "Пульс 95",
        OkkoFuelCategory.Diesel => "ДП",
        OkkoFuelCategory.DieselPulls => "Пульс ДП",
        OkkoFuelCategory.Gas => "Пропан",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "unmapped grade"),
    };

    /// <summary>
    /// Extracts the leading numeric OKKO product code from a QR payload (the digits before the
    /// first '$'), or null when the payload is absent or not in the OKKO product-code shape.
    /// <para>
    /// The product code is a fuel-SKU indicator shared across many vouchers, not the redeemable
    /// bearer material, so it is safe to surface in logs and error rows — unlike the full payload,
    /// which <see cref="ImportVouchersCommandHandler.DescribeQrPayload"/> deliberately fingerprints.
    /// </para>
    /// </summary>
    public static string? TryGetProductCode(string? qrPayload)
    {
        if (string.IsNullOrEmpty(qrPayload))
            return null;

        var match = ProductCodeRegex.Match(qrPayload);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Maps an OKKO QR product code to a canonical category, or null when the code is unmapped.</summary>
    public static OkkoFuelCategory? CategoryFromQrCode(string? productCode) => productCode switch
    {
        "9018" or "9518" => OkkoFuelCategory.Diesel,
        "45290" => OkkoFuelCategory.DieselPulls,
        "9015" or "9016" or "9515" or "9009" => OkkoFuelCategory.A95,
        "9019" or "9020" => OkkoFuelCategory.Gas,
        _ => null
    };

    /// <summary>Classifies a DB fuel-type display name (clean text) into a canonical category.</summary>
    public static OkkoFuelCategory? CategoryFromName(string? name) => Classify(name, strict95: false);

    /// <summary>
    /// Maps an OKKO <c>fuel_code</c> — the short stable key the public site publishes next to every
    /// price on <c>okko.ua/fuels</c> ("A-95", "Pulls Diesel", "SPBT") — onto a canonical category.
    /// <para>
    /// The price scraper reads those codes rather than the rendered label, because the label is
    /// localized per request while the code is not. Matching on the code also keeps this mapping out
    /// of <see cref="Classify"/>, whose substring heuristics would misread several of them: "DP" is
    /// diesel yet contains no "дп"/"diesel" token, and "Pulls 100" (aviation jet fuel) would be read
    /// as neither a petrol grade nor a diesel.
    /// </para>
    /// <para>
    /// Codes outside the catalog's scope return null rather than a guess: "Pulls 100" is jet fuel we
    /// do not sell and "AdBlue" is an additive, not a fuel grade.
    /// </para>
    /// </summary>
    public static OkkoFuelCategory? CategoryFromSiteFuelCode(string? fuelCode) => fuelCode?.Trim() switch
    {
        "A-95" => OkkoFuelCategory.A95,
        "Pulls 95" => OkkoFuelCategory.A95Pulls,
        "DP" => OkkoFuelCategory.Diesel,
        "Pulls Diesel" => OkkoFuelCategory.DieselPulls,
        "SPBT" => OkkoFuelCategory.Gas,
        _ => null,
    };

    /// <summary>Classifies raw OCR voucher text into a canonical category (95 must be anchored to an A marker).</summary>
    public static OkkoFuelCategory? CategoryFromText(string? text) => Classify(text, strict95: true);

    private static OkkoFuelCategory? Classify(string? source, bool strict95)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var lower = source.ToLowerInvariant();

        // "ГАЗ" and its Latin spellings. "SPBT" is the brand's own code for LPG (сжижений пропан),
        // and "LPG" is how an operator writes it; neither contains "газ", so without them a fuel
        // named after the code classifies as no grade at all.
        if (lower.Contains("газ") || lower.Contains("gas") || lower.Contains("лпг")
            || lower.Contains("lpg") || lower.Contains("spbt"))
            return OkkoFuelCategory.Gas;

        var hasPulls = lower.Contains("pulls") || lower.Contains("пулс");
        var hasDiesel = DieselToken.IsMatch(lower) || lower.Contains("дизель") || lower.Contains("diesel");
        var has95 = strict95 ? Petrol95InText.IsMatch(lower) : lower.Contains("95");

        // Premium (PULLS) categories are checked before their base categories so that
        // "ДП PULLS" resolves to DieselPulls rather than Diesel, and "Pulls 95" to A95Pulls.
        if (hasPulls && hasDiesel) return OkkoFuelCategory.DieselPulls;
        if (hasPulls && has95) return OkkoFuelCategory.A95Pulls;
        if (hasDiesel) return OkkoFuelCategory.Diesel;
        if (has95) return OkkoFuelCategory.A95;

        return null;
    }

    /// <summary>
    /// Resolves the OKKO fuel type for a parsed voucher. The QR product code is the primary,
    /// stable signal; raw OCR text is the fallback when the QR is missing or its code is unmapped.
    /// <para>
    /// Returns null (rather than guessing a default) when neither signal yields a category the
    /// Brand catalog actually contains, so the caller rejects the row with a clear message
    /// instead of silently importing it under the wrong fuel type.
    /// </para>
    /// <para>
    /// <paramref name="byProductCode"/> is the exact path and it is checked first: a code that
    /// <c>fuel_type_product_codes</c> claims resolves to that fuel with no interpretation at all —
    /// no category, no name matching, nothing that can be wrong about spelling. It is optional so
    /// that a catalogue with no rows behaves exactly as it did before the table existed, and
    /// <c>CategoryFromQrCode</c> stays as the second reading of the same code for those rows.
    /// </para>
    /// </remarks>
    public static FuelTypeEntity? ResolveFuelType(
        string? rawText,
        string? qrPayload,
        IReadOnlyCollection<FuelTypeEntity> okkoFuelTypes,
        IReadOnlyDictionary<string, FuelTypeEntity>? byProductCode = null)
    {
        var productCode = TryGetProductCode(qrPayload);

        if (productCode is not null
            && byProductCode is not null
            && byProductCode.TryGetValue(productCode, out var byCode))
        {
            return byCode;
        }

        var category = CategoryFromQrCode(productCode) ?? CategoryFromText(rawText);

        if (category is null)
            return null;

        return okkoFuelTypes.FirstOrDefault(ft => CategoryFromName(ft.Name) == category);
    }
}
