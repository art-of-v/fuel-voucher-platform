namespace FuelFlow.Features.Brands;

/// <summary>
/// Recognises a rehearsal package from how it is named, used to refuse an obviously-rehearsal row
/// being created as a live one.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> the read-side band-aid it looks like. The catalogue filter for rehearsal rows
/// is the pool match in <c>GetPublicPackagesQueryHandler</c> plus <c>FuelPackage.IsTestData</c>, and
/// that is correct. This exists because the flag is optional: nothing in the API forces a creator to
/// set it, and it defaults to <c>false</c>, which means "sell this".
/// </para>
/// <para>
/// Production has absorbed two rows that way — <c>QA-CHECK-95</c> at 1 UAH, and
/// <c>А95 ЄВРО (ТЕСТ 1ГРН)</c> at 1 UAH as <c>test-1uah-okko-a95eu</c> — each served to real customers by
/// the same anonymous endpoint the marketing site reads. Both were rehearsal data that nobody meant
/// to publish; both were published because the flag said "not test" and nothing disagreed.
/// </para>
/// <para>
/// The tokens are this repository's own seeds' convention plus what an operator typed by hand:
/// <c>scripts/seed-qa-catalogue.sql</c> writes ids like <c>qa-pkg-&lt;id&gt;</c> and names like
/// <c>&lt;fuel&gt;(QA)</c>. Word-ish forms are listed alongside bare prefixes so a legitimate fuel
/// whose name merely contains a token as a substring is not caught.
/// </para>
/// <para>
/// One place on purpose. A second list in a second language drifts, and that drift is exactly how a
/// site-side filter that used to carry this rule stopped matching while looking perfectly healthy.
/// </para>
/// </remarks>
public static class RehearsalMarkers
{
    private static readonly string[] Tokens =
    [
        "qa-", "qa_", "(qa)", "[qa]",
        "test-", "test_", "(test)", "[test]",
    ];

    private const string CyrillicToken = "тест";

    /// <summary>
    /// Whether a package id or fuel name reads as rehearsal data.
    /// </summary>
    public static bool LooksLikeRehearsal(string? id, string? fuelName)
        => Matches(id) || Matches(fuelName)
           || MatchesCyrillic(id) || MatchesCyrillic(fuelName);

    /// <summary>
    /// The token that matched, for a message an operator can act on. Null when nothing did.
    /// </summary>
    public static string? MatchedToken(string? id, string? fuelName)
        => Match(id) ?? Match(fuelName) ?? MatchCyrillic(id) ?? MatchCyrillic(fuelName);

    private static string? Match(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Tokens.FirstOrDefault(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static string? MatchCyrillic(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(CyrillicToken, StringComparison.OrdinalIgnoreCase)
                ? CyrillicToken
                : null;

    private static bool Matches(string? value) => Match(value) is not null;
    private static bool MatchesCyrillic(string? value) => MatchCyrillic(value) is not null;
}