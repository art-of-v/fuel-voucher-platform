using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Security.Attestation;

/// <summary>
/// The encodings the attestation protocol uses. Centralised because every one of them is a place
/// where a subtle mismatch between client and server turns into a proof that never verifies.
/// </summary>
public static class AttestationEncoding
{
    /// <summary>
    /// Decodes standard base64 and the web-safe, unpadded variant the mobile clients produce.
    /// </summary>
    public static byte[] DecodeBase64(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');

        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            0 => normalized,
            _ => throw new FormatException("The value is not valid base64."),
        };

        return Convert.FromBase64String(normalized);
    }

    /// <summary>Standard base64, which is what the database stores and the client sends for Apple keys.</summary>
    public static string ToBase64(ReadOnlySpan<byte> value) => Convert.ToBase64String(value);

    /// <summary>Base64url without padding, which is what Play Integrity and the challenge use.</summary>
    public static string ToBase64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public static byte[] Sha256(ReadOnlySpan<byte> value) => SHA256.HashData(value);

    /// <summary>A 32-byte SHA-256 digest in base64url form.</summary>
    public static bool IsBase64UrlSha256(string? value)
    {
        if (value is not { Length: 43 })
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Compares two short strings without an early exit. The values compared here are hashes and
    /// codes, not secrets, but the habit is cheap and removes a class of timing question.
    /// </summary>
    public static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
