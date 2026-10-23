using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.Features.Auth.Security;

/// <summary>
/// Hashes a short verification code with the server-side OTP pepper.
/// </summary>
/// <remarks>
/// <para>
/// A six-digit code has only a million possible values, so an unprefixed SHA-256 digest of it is
/// not a secret: anyone who reads the <c>verification_codes</c> table - or a backup of it - can
/// search that space in milliseconds. The pepper is the missing half of the key: it lives in the
/// deployment configuration and never in the database, so a leaked dump alone cannot test a
/// candidate. Refresh tokens keep the bare <see cref="SharedKernel.Security.SecretsHasher"/>,
/// because 128 random bits cannot be searched and a slow hash there buys nothing.
/// </para>
/// <para>
/// HMAC-SHA256 rather than a memory-hard KDF: the cost that matters is the value the attacker does
/// not have, and the verification path is on the sign-in critical path. The stored value is
/// versioned (<c>v2:</c>) so a digest written by an older scheme is recognisable - it can never
/// verify, and it can be invalidated deliberately instead of being compared hopefully.
/// </para>
/// </remarks>
public static class OtpCodeHasher
{
    /// <summary>The marker every peppered digest starts with.</summary>
    public const string PepperedPrefix = "v2:";

    /// <summary>
    /// Hash <paramref name="code"/> with <paramref name="pepper"/> for storage.
    /// </summary>
    /// <exception cref="InvalidOperationException">The pepper is blank. A blank pepper is an
    /// unprefixed hash wearing a version marker; it is refused rather than silently accepted.</exception>
    public static string Hash(string code, string pepper)
    {
        if (string.IsNullOrWhiteSpace(pepper))
        {
            throw new InvalidOperationException(
                "The OTP pepper is not configured. A verification code cannot be hashed without it: "
                + "an unpeppered digest of a six-digit code is searchable offline. Set Auth:OtpPepper "
                + "(OTP_PEPPER in the deploy env).");
        }

        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(pepper), Encoding.UTF8.GetBytes(code));
        return PepperedPrefix + Convert.ToBase64String(mac);
    }

    /// <summary>
    /// Whether <paramref name="storedHash"/> was written by <see cref="Hash"/>. A value without the
    /// marker is a pre-pepper digest: no submitted code can make it verify.
    /// </summary>
    public static bool IsPeppered(string? storedHash) =>
        storedHash is not null && storedHash.StartsWith(PepperedPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Compare a submitted code against a stored digest in constant time.
    /// </summary>
    /// <remarks>
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> rather than <c>==</c>: the stored
    /// digest is derived from a secret the caller chose, and a byte-by-byte comparison lets the
    /// caller learn the matching prefix from the response time. A malformed or legacy digest is
    /// simply not a match; the caller decides whether to burn it.
    /// </remarks>
    public static bool Verify(string? storedHash, string code, string pepper)
    {
        if (!IsPeppered(storedHash))
            return false;

        var expected = Encoding.UTF8.GetBytes(Hash(code, pepper));
        var actual = Encoding.UTF8.GetBytes(storedHash!);

        return expected.Length == actual.Length
               && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
