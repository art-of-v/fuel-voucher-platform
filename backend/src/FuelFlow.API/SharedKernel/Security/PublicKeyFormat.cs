using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// Reads a public key from the shapes a deployment can realistically hold, so the verifier and the
/// startup guard agree on what "configured" means.
/// </summary>
/// <remarks>
/// Exists because a configured-looking key is not a usable key. Monobank returns 240 characters of
/// base64-encoded PEM from <c>GET /api/merchant/pubkey</c>; a value truncated to 100 characters by a
/// hand-edited env file still passes every "is it set?" check, and then every webhook fails at the
/// exact point verification runs - which is how a live merchant ended up unable to confirm any
/// payment, silently, with the reconciliation poll as the only thing completing orders.
/// <para>
/// Accepts a raw PEM, a base64-encoded PEM, or raw base64 SubjectPublicKeyInfo bytes.
/// </para>
/// </remarks>
public static class PublicKeyFormat
{
    /// <summary>
    /// Attempts to read <paramref name="key"/> as a PEM-encoded public key.
    /// </summary>
    /// <remarks>
    /// Deliberately stricter than the verify path, which is lenient because a lenient key that
    /// fails to verify is harmless. Here the answer decides whether production boots at all, so a
    /// value that cannot be read is a configuration error rather than a signature to reject.
    /// </remarks>
    public static bool TryRead(string? key, out string normalizedPem)
    {
        normalizedPem = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var trimmed = key.Trim();

        if (trimmed.StartsWith("-----", StringComparison.Ordinal))
        {
            // Only accept it if the PEM is complete - a body with no END line is a truncation.
            if (!trimmed.Contains("-----END", StringComparison.Ordinal))
            {
                return false;
            }

            normalizedPem = trimmed;
            return true;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(trimmed);
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length == 0)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(decoded).Trim();
        if (text.StartsWith("-----", StringComparison.Ordinal))
        {
            if (!text.Contains("-----END", StringComparison.Ordinal))
            {
                return false;
            }

            normalizedPem = text;
            return true;
        }

        normalizedPem = new string(PemEncoding.Write("PUBLIC KEY", decoded));
        return true;
    }

    /// <summary>
    /// Whether <paramref name="key"/> is a readable public key. Used by the startup guard.
    /// </summary>
    public static bool IsReadable(string? key) => TryRead(key, out _);
}