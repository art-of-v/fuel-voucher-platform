using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// Hashes low-entropy credentials (OTP codes, refresh tokens) for storage so a
/// database leak does not yield usable secrets. SHA-256 is sufficient for
/// refresh tokens (128 bits of entropy). OTP codes are only 6 digits — hashing
/// protects against casual leaks; offline brute-force of the small code space
/// is additionally mitigated by short expiry, per-code attempt limits and
/// rate limiting.
/// </summary>
public static class SecretsHasher
{
    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}