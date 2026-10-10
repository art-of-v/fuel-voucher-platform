namespace FuelFlow.Features.Contracts.SharedModels;

/// <summary>
/// Format checks for the identifiers a legal entity carries.
/// </summary>
/// <remarks>
/// <para>
/// Server-side is the authoritative gate: every write path for a legal entity runs through
/// <c>LegalEntityController</c>, so validating there covers create, the profile upsert and the
/// scoped edit at once. The mobile and admin forms mirror these rules for instant feedback, but a
/// client that forgets is caught here.
/// </para>
/// <para>
/// Deliberately format-only. Whether a well-formed code is *actually* registered is a registry
/// lookup - an external dependency, and out of scope here.
/// </para>
/// </remarks>
public static class LegalEntityIdentifier
{
    /// <summary>ЄДРПОУ (a legal entity's registry code): exactly 8 digits.</summary>
    public const int EdrpouLength = 8;

    /// <summary>ІПН ПДВ (VAT, optional): exactly 12 digits.</summary>
    public const int VatLength = 12;

    /// <summary>
    /// True when <paramref name="edrpou"/> is 8 ASCII digits. Whitespace around the value is
    /// tolerated because operators paste these out of documents and spreadsheets.
    /// </summary>
    public static bool IsValidEdrpou(string? edrpou)
        => IsAllDigits(edrpou, EdrpouLength);

    /// <summary>
    /// True when <paramref name="vat"/> is acceptable: empty (VAT is optional) or 12 ASCII digits.
    /// </summary>
    public static bool IsValidVat(string? vat)
        => string.IsNullOrWhiteSpace(vat) || IsAllDigits(vat, VatLength);

    /// <summary>
    /// The problem with <paramref name="edrpou"/>, or <c>null</c> when it is acceptable.
    /// </summary>
    public static string? DescribeEdrpouProblem(string? edrpou)
        => IsValidEdrpou(edrpou)
            ? null
            : $"EDRPOU must be exactly {EdrpouLength} digits";

    /// <summary>
    /// The problem with <paramref name="vat"/>, or <c>null</c> when it is acceptable. A present-but-
    /// malformed VAT is rejected rather than ignored: an operator who typed something meant to be a
    /// VAT should hear that it is wrong, not have it silently dropped.
    /// </summary>
    public static string? DescribeVatProblem(string? vat)
        => IsValidVat(vat)
            ? null
            : $"VAT must be exactly {VatLength} digits";

    /// <summary>
    /// True when the value trims to exactly <paramref name="length"/> ASCII digits. Rejects letters,
    /// spaces inside the number, punctuation and any other locale's digits (Cyrillic numerals,
    /// fullwidth digits), because those are all a paste from the wrong source.
    /// </summary>
    private static bool IsAllDigits(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        return trimmed.Length == length && trimmed.All(c => c is >= '0' and <= '9');
    }
}