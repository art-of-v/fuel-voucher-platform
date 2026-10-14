using System.Security.Cryptography;
using System.Globalization;
using System.Text;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// Signs and verifies the <c>/start</c> payload that binds a customer's private Telegram chat to a
/// FuelFlow account, and builds the deep link the mobile app hands to Telegram.
/// <para>
/// <b>The app never signs anything.</b> It asks <c>GET /api/feedback/deep-link</c> for a ready-made
/// URL and opens it. A signing key inside a shipped binary is a public signing key, which would let
/// anyone start a chat impersonating any account - so the secret lives only on the server, and the
/// only thing a leaked deep link proves is "this person used our app", which the 30-day expiry then
/// retires.
/// </para>
/// <para>
/// Format: <c>{userId:N}_{unixSeconds}_{base64url(hmac-sha256)}</c>, where the MAC covers the first
/// two fields. Telegram's deep-linking accepts only <c>A-Za-z0-9_-</c> in the payload and caps it
/// at 64 characters, which base64url and an underscore separator satisfy by construction - and which
/// is why the GUID is written in "N" form (32, not 36) and the signature truncated: see
/// <see cref="SignatureBytes"/>.
/// <para>
/// The character set is not a style preference. A payload containing anything else - a dot, which the
/// first version used - is not rejected by Telegram: the client silently drops it and delivers a bare
/// <c>/start</c>, so the chat binds to nothing and nothing anywhere reports a failure.
/// </para>
/// </para>
/// </summary>
public static class FeedbackDeepLink
{
    /// <summary>
    /// Bytes of HMAC kept in the payload. 12 of SHA-256's 32, and that is a budget decision as much
    /// as a security one: a 32-character GUID ("N" format) plus a 10-digit timestamp and two dots is
    /// 44 characters, and Telegram allows 64 for a /start payload - which leaves 19 for the
    /// signature. 12 bytes is 16 base64url characters, the largest round size that fits with room to
    /// spare. 96 bits of truncated SHA-256 is still far beyond what this needs to resist: the value
    /// is not a bearer secret, it is an assertion that survives a leaked link for its TTL.
    /// </summary>
    private const int SignatureBytes = 12;

    /// <summary>
    /// Telegram's hard cap on a <c>/start</c> payload. Exceeding it is not an error on Telegram's side
    /// either - the parameter is dropped - so the budget is enforced here, where it can still be thrown.
    /// </summary>
    private const int MaxPayloadLength = 64;

    /// <summary>
    /// Field separator inside the payload. Underscore rather than a dot, because Telegram allows
    /// <c>_</c> and <c>-</c> but **not** <c>.</c>. Note the signature is base64url and may itself
    /// contain <c>_</c>, so parsing anchors on the fixed-width GUID and the all-digit timestamp rather
    /// than splitting the string on the separator.
    /// </summary>
    private const char Separator = '_';

    /// <summary>Length of a GUID in "N" form - the only format that fits the payload budget.</summary>
    private const int GuidNLength = 32;

    /// <summary>
    /// Signs a payload for <paramref name="userId"/>, valid until
    /// <paramref name="ttlDays"/> from <paramref name="nowUtc"/>. An empty or too-short key throws
    /// rather than producing a link that verifies against nothing.
    /// </summary>
    public static string CreatePayload(Guid userId, string signingKey, TimeSpan ttlDays, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);

        // The key must have real entropy; a short configured value would otherwise silently make
        // every signature trivially brute-forceable.
        if (Encoding.UTF8.GetByteCount(signingKey) < 16)
        {
            throw new ArgumentException(
                "The feedback deep-link signing key must be at least 16 bytes.", nameof(signingKey));
        }

        var body = $"{userId:N}{Separator}{ToUnixSeconds(nowUtc)}";
        var signature = Sign(body, signingKey);

        var payload = $"{body}{Separator}{Base64UrlEncode(signature)}";

        if (payload.Length > MaxPayloadLength)
        {
            throw new InvalidOperationException(
                $"The signed deep-link payload is {payload.Length} characters; Telegram accepts at most {MaxPayloadLength}.");
        }

        // The single most important assertion in this file. Telegram's deep-linking spec allows only
        // A-Z, a-z, 0-9, "_" and "-" in a /start payload, and it does not reject a violation - it drops
        // the payload and delivers a bare "/start". The first version of this format used '.' as the
        // separator, which is not in that set, so every link bound nothing and the chat stayed anonymous
        // with no error anywhere: the bot saw a start command without arguments, the webhook answered 200,
        // and getWebhookInfo stayed clean. See PayloadIsTelegramLegal for the regression test.
        if (!IsTelegramLegal(payload))
        {
            throw new InvalidOperationException(
                "The signed deep-link payload contains characters Telegram does not accept in a /start payload.");
        }

        return payload;
    }

    /// <summary>
    /// Telegram's own <c>/start</c> payload grammar: 1-64 characters, only <c>A-Z a-z 0-9 _ -</c>.
    /// A payload outside it is not rejected - it is silently discarded by the client.
    /// </summary>
    private static bool IsTelegramLegal(string payload) =>
        payload.Length is > 0 and <= MaxPayloadLength
        && payload.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>
    /// The deep link the mobile app opens: <c>https://t.me/{bot}?start={payload}</c>. Returns null
    /// when the feature is not configured, so callers can report "unavailable" instead of handing the
    /// user a link that opens nothing.
    /// </summary>
    /// <remarks>
    /// The parameters are nullable because the body already treats them that way —
    /// <see cref="string.IsNullOrWhiteSpace(string)"/> is true for null — and a bot username that
    /// never got configured arrives exactly that way. Declaring them non-nullable said otherwise
    /// and cost the test that proves the case a suppression it did not need.
    /// </remarks>
    public static string? BuildDeepLink(string? botUsername, string? payload)
    {
        if (string.IsNullOrWhiteSpace(botUsername) || string.IsNullOrWhiteSpace(payload))
            return null;

        var username = botUsername.TrimStart('@');
        return $"https://t.me/{username}?start={payload}";
    }

    /// <summary>
    /// Verifies a payload and returns the account it names. Null for anything that does not check
    /// out - malformed, forged, or expired. The caller treats null as "unbound chat" rather than as
    /// an error: somebody who found the bot by name and started typing still gets to be heard.
    /// </summary>
    public static Guid? VerifyPayload(
        string? payload,
        string signingKey,
        TimeSpan ttlDays,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(signingKey))
            return null;

        if (payload.Length > MaxPayloadLength)
            return null;

        // Splitting on the separator is not safe: the signature is base64url and can contain '_'
        // itself. The layout is fixed-width at the front instead - a 32-character "N" GUID, then a
        // separator, then an all-digit timestamp - which makes the two separators unambiguous.
        if (payload.Length <= GuidNLength + 2)
            return null;

        var guidPart = payload[..GuidNLength];
        if (payload[GuidNLength] != Separator)
            return null;

        var timestampStart = GuidNLength + 1;
        var separatorIndex = payload.IndexOf(Separator, timestampStart);
        if (separatorIndex <= timestampStart)
            return null;

        var timestampPart = payload[timestampStart..separatorIndex];
        var presented = payload[(separatorIndex + 1)..];

        byte[] presentedBytes;
        try
        {
            presentedBytes = Base64UrlDecode(presented);
        }
        catch (FormatException)
        {
            return null;
        }

        var expected = Sign($"{guidPart}{Separator}{timestampPart}", signingKey);

        // FixedTimeEquals rather than == : the comparison happens on attacker-chosen input, and a
        // short-circuiting compare leaks how many leading bytes matched.
        if (!CryptographicOperations.FixedTimeEquals(presentedBytes, expected))
            return null;

        if (!Guid.TryParseExact(guidPart, "N", out var userId))
            return null;

        if (!long.TryParse(timestampPart, NumberStyles.None, CultureInfo.InvariantCulture, out var issuedAt))
            return null;

        // Accept a small amount of clock skew in the other direction, so a phone a few minutes fast
        // does not invalidate a link the server just minted.
        if (issuedAt > ToUnixSeconds(nowUtc) + 60)
            return null;

        var age = nowUtc - DateTimeOffset.FromUnixTimeSeconds(issuedAt).UtcDateTime;
        if (age > ttlDays)
            return null;

        return userId;
    }

    private static byte[] Sign(string body, string signingKey)
    {
        var key = Encoding.UTF8.GetBytes(signingKey);
        var data = Encoding.UTF8.GetBytes(body);
        var mac = HMACSHA256.HashData(key, data);
        return mac[..SignatureBytes];
    }

    private static long ToUnixSeconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '='));
    }
}