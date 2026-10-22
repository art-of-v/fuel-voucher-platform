using System.Text.RegularExpressions;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// Tells a real secret from a placeholder that nobody replaced.
/// </summary>
/// <remarks>
/// <para>
/// The failure this exists for: the production env template ships
/// <c>JWT_SECRET=CHANGE_ME_at_least_32_characters_long_random_string</c>, and the guard that was
/// supposed to catch it compared the value against a DIFFERENT literal - one that no template has
/// ever shipped. The template's value is 53 characters, so it passed the length check as well, and
/// the API booted on it, silently, in Production. The repository is public until 2026-11-01, so
/// that string is on the internet: an HS256 access token signed with it can be forged by anybody,
/// which is full staff console access - refunds, payment placement, customer PII, voucher QR
/// payloads - with the whole session model (rotation, reuse detection, device binding) bypassed,
/// because the forger never holds a refresh token.
/// </para>
/// <para>
/// So the rule is the PATTERN, not one literal: a value that reads like a placeholder is refused,
/// whatever wording it uses. A literal list is a list that goes stale, and the one that went stale
/// here is the reason this class exists. The deploy template is the second half of the defence:
/// <c>deploy/preflight.sh</c> compares the deployed env file against the template's own values
/// verbatim, so editing the template to hide the placeholder does not defeat the check.
/// </para>
/// <para>
/// The pattern is deliberately narrow. A false positive here blocks a deploy over a secret that is
/// perfectly good, and a gate that cries wolf is a gate people learn to skip - so only the wordings
/// that are actually placeholders are matched. A random base64 or hex secret cannot contain them
/// by accident: the shortest of them is four characters long and needs a specific letter in a
/// specific position.
/// </para>
/// </remarks>
public static class PlaceholderSecret
{
    /// <summary>
    /// The wordings a placeholder takes. Case-insensitive, and matched anywhere in the value -
    /// a secret that STARTS with a placeholder word and then carries a real suffix is still a
    /// value nobody chose.
    /// </summary>
    private static readonly Regex PlaceholderPattern = new(
        @"(change[_-]?me|replace[_-]?me|insert[_-]?here|placeholder|example|your[_-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Whether the value reads like a placeholder rather than a secret.
    /// </summary>
    /// <param name="value">The configured value. A missing or blank value is not a placeholder -
    /// it is a missing value, and the caller refuses that separately.</param>
    public static bool LooksLikePlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return PlaceholderPattern.IsMatch(value);
    }
}
