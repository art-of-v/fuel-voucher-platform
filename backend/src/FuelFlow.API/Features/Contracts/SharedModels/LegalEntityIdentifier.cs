using System.Globalization;

namespace FuelFlow.Features.Contracts.SharedModels;

/// <summary>
/// Format checks for the identifiers a legal entity carries.
/// </summary>
/// <remarks>
/// <para>
/// Server-side is the authoritative gate: every write path for a legal entity runs through one
/// shared gate (<c>LegalEntityDetails.DescribeProblem</c>), so validating there covers create, the
/// profile upsert and the scoped edit at once. The mobile and admin forms mirror these rules for
/// instant feedback, but a client that forgets is caught here.
/// </para>
/// <para>
/// Two layers, deliberately distinct. The <em>format</em> rule (#128) is a shape check. The
/// <em>control digit</em> (#266) is a checksum that catches a mistyped or transposed digit - the
/// defect format alone cannot see, and a permanent one, because
/// <c>legal_entities.edrpou</c> carries a unique index.
/// </para>
/// <para>
/// What neither layer does is confirm the code is actually <em>registered</em>. That is a registry
/// lookup - an external dependency, and out of scope here. The checksum is self-contained, so it is
/// in scope; the registry is not.
/// </para>
/// </remarks>
public static class LegalEntityIdentifier
{
    /// <summary>ЄДРПОУ (a legal entity's registry code): exactly 8 digits.</summary>
    public const int EdrpouLength = 8;

    /// <summary>ІПН ПДВ (VAT, optional): exactly 12 digits.</summary>
    public const int VatLength = 12;

    /// <summary>
    /// The first seven digits of the code carry the registry number; the eighth is a control digit
    /// computed from them.
    /// </summary>
    private const int EdrpouDigitsBeforeControl = 7;

    /// <summary>
    /// The module the checksum is taken against.
    /// </summary>
    private const int EdrpouChecksumModulus = 11;

    /// <summary>
    /// The control digit is defined as the remainder modulo 11, so it can only ever be 0-10. A
    /// remainder of 10 cannot be a single digit and triggers the second weight sequence below; a
    /// second remainder of 10 resolves to 0.
    /// </summary>
    private const int EdrpouChecksumUnresolvable = 10;

    /// <summary>
    /// Codes in the inclusive range <c>30 000 000</c>-<c>60 000 000</c> are weighted with the
    /// sequence rotated by one, every other code with the plain ordinal sequence.
    /// </summary>
    private const long EdrpouRotatedWeightsFrom = 30_000_000;

    private const long EdrpouRotatedWeightsTo = 60_000_000;

    /// <summary>Positional weights for the first seven digits of a code outside the rotated range.</summary>
    private static readonly int[] EdrpouOrdinalWeights = [1, 2, 3, 4, 5, 6, 7];

    /// <summary>
    /// The ordinal sequence shifted by two, used on the second pass when the first remainder is 10.
    /// </summary>
    private static readonly int[] EdrpouOrdinalWeightsShifted = [3, 4, 5, 6, 7, 8, 9];

    /// <summary>Positional weights for a code inside the rotated range.</summary>
    private static readonly int[] EdrpouRotatedWeights = [7, 1, 2, 3, 4, 5, 6];

    /// <summary>The rotated sequence shifted by two, for the second pass.</summary>
    private static readonly int[] EdrpouRotatedWeightsShifted = [9, 3, 4, 5, 6, 7, 8];

    /// <summary>
    /// True when <paramref name="edrpou"/> is 8 ASCII digits <em>and</em> its control digit checks
    /// out. Whitespace around the value is tolerated because operators paste these out of documents
    /// and spreadsheets.
    /// </summary>
    public static bool IsValidEdrpou(string? edrpou)
        => NormalizeEdrpou(edrpou) is { } normalized && HasValidControlDigit(normalized);

    /// <summary>
    /// True when the value is acceptable: empty (VAT is optional) or 12 ASCII digits.
    /// </summary>
    public static bool IsValidVat(string? vat)
        => string.IsNullOrWhiteSpace(vat) || IsAllDigits(vat, VatLength);

    /// <summary>
    /// The problem with <paramref name="edrpou"/>, or <c>null</c> when it is acceptable.
    /// </summary>
    public static string? DescribeEdrpouProblem(string? edrpou)
    {
        if (NormalizeEdrpou(edrpou) is not { } normalized)
        {
            return $"EDRPOU must be exactly {EdrpouLength} digits";
        }

        // A distinct message: "8 digits" would not help somebody whose digits are all correct.
        return HasValidControlDigit(normalized)
            ? null
            : "EDRPOU control digit does not match the first seven digits";
    }

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
    /// True when <paramref name="value"/> is exactly eight ASCII digits, returned trimmed; otherwise
    /// <c>null</c>, which is how the format layer rejects.
    /// </summary>
    private static string? NormalizeEdrpou(string? value)
        => IsAllDigits(value, EdrpouLength) ? value!.Trim() : null;

    /// <summary>
    /// True when the eighth digit of a well-formed code equals the control digit computed from the
    /// first seven.
    /// </summary>
    /// <remarks>
    /// The weight sequence is chosen by the numeric value of the <em>whole eight-digit code</em>, not
    /// of the seven-digit prefix. That distinction is easy to get wrong and every published
    /// description of this algorithm is ambiguous about it; getting it backwards makes four of the
    /// pinned codes in <c>LegalEntityIdentifierTests</c> fail, which is how the rule was settled.
    /// </remarks>
    private static bool HasValidControlDigit(string normalized)
    {
        var registryNumber = normalized[..EdrpouDigitsBeforeControl];
        var controlDigit = normalized[EdrpouDigitsBeforeControl] - '0';

        // The whole code, check digit included, decides the weights. Reading only the prefix is the
        // trap: it selects the sequence for codes the registry actually weighted the other way.
        var rotated = long.Parse(normalized, CultureInfo.InvariantCulture) switch
        {
            >= EdrpouRotatedWeightsFrom and <= EdrpouRotatedWeightsTo => true,
            _ => false,
        };

        var firstPass = Remainder(registryNumber, rotated
            ? EdrpouRotatedWeights
            : EdrpouOrdinalWeights);

        if (firstPass != EdrpouChecksumUnresolvable)
        {
            return firstPass == controlDigit;
        }

        // A remainder of 10 is not a digit. Recompute on the shifted sequence; a second 10 resolves
        // to 0 rather than failing, because that is what the registry does.
        var secondPass = Remainder(registryNumber, rotated
            ? EdrpouRotatedWeightsShifted
            : EdrpouOrdinalWeightsShifted);

        var resolved = secondPass == EdrpouChecksumUnresolvable ? 0 : secondPass;

        return resolved == controlDigit;
    }

    /// <summary>
    /// The weighted sum of <paramref name="registryNumber"/>'s digits modulo the checksum modulus.
    /// </summary>
    private static int Remainder(string registryNumber, int[] weights)
    {
        var sum = 0;

        for (var position = 0; position < EdrpouDigitsBeforeControl; position++)
        {
            sum += (registryNumber[position] - '0') * weights[position];
        }

        return sum % EdrpouChecksumModulus;
    }

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