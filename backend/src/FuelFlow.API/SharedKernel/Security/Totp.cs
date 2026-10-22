using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.SharedKernel.Security;

/// <summary>
/// Time-based one-time passwords, RFC 6238 (TOTP over the RFC 4226 HOTP construction).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> A staff sign-in used to be a single factor: a code that arrived in a
/// mailbox. The mailbox is the weakest channel the platform has - it is the one an attacker can
/// influence (forwarding rules, spam-folder rules, a provider breach) and the one a phishing
/// campaign reaches. A second factor has to come from somewhere the attacker does not hold, and
/// the cheapest such thing is a code computed by an authenticator app on the phone.
/// </para>
/// <para>
/// <b>Why not a library.</b> The finding says "no new dependency": this is RFC 6238 with a
/// HMAC-SHA1, a Base32 alphabet and a 30-second counter - about a hundred lines that depend on
/// nothing but <see cref="HMACSHA1"/> and <see cref="RandomNumberGenerator"/>. A package would
/// add a supply-chain surface to the authentication path to save those lines.
/// </para>
/// <para>
/// <b>Parameters.</b> HMAC-SHA1, 6 digits, a 30-second step, a 20-byte (160-bit) secret. The
/// secret length is the RFC's recommendation; the digit count is what every authenticator app
/// expects. SHA-1 is not a weakness here: it is the HMAC, not the hash, and HMAC-SHA1 has no
/// known attack - the RFC itself specifies it as the default.
/// </para>
/// </remarks>
public static class Totp
{
    /// <summary>Secret length in bytes. 160 bits, the RFC 4226 recommendation.</summary>
    public const int SecretSizeBytes = 20;

    /// <summary>How many digits the code has.</summary>
    public const int Digits = 6;

    /// <summary>How long one code is valid, in seconds.</summary>
    public const int PeriodSeconds = 30;

    /// <summary>How many steps on either side of "now" are accepted, to absorb clock drift.</summary>
    public const int DefaultWindow = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A fresh secret, Base32-encoded - the form every authenticator app reads.
    /// </summary>
    public static string GenerateSecret()
    {
        return EncodeBase32(RandomNumberGenerator.GetBytes(SecretSizeBytes));
    }

    /// <summary>
    /// Encodes bytes as Base32 without padding (RFC 4648). No padding, because the
    /// authenticator apps strip it and a secret that differs only in its padding is a
    /// support ticket.
    /// </summary>
    public static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                builder.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }

            // Keep only the bits that have not been emitted yet: the buffer would otherwise
            // grow past the width of an int and the overflow would corrupt the next symbol.
            buffer &= (1 << bitsLeft) - 1;
        }

        if (bitsLeft > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes a Base32 secret. Lenient about case, spaces, dashes and padding, because a
    /// human types this key into an app by hand.
    /// </summary>
    /// <exception cref="FormatException">The value is not Base32.</exception>
    public static byte[] DecodeBase32(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .TrimEnd('=')
            .ToUpperInvariant();

        if (normalized.Length == 0)
            throw new FormatException("The secret is empty.");

        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in normalized)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
                throw new FormatException($"'{c}' is not a Base32 character.");

            buffer = (buffer << 5) | index;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }

            buffer &= (1 << bitsLeft) - 1;
        }

        return output.ToArray();
    }

    /// <summary>
    /// The code for a Base32 secret at a given instant.
    /// </summary>
    public static string ComputeCode(string base32Secret, DateTime utcNow)
    {
        return ComputeCode(DecodeBase32(base32Secret), utcNow);
    }

    /// <summary>
    /// The code for a raw secret at a given instant.
    /// </summary>
    public static string ComputeCode(byte[] secret, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var counter = (long)(utcNow.ToUniversalTime() - UnixEpoch).TotalSeconds / PeriodSeconds;
        var hash = ComputeHash(secret, counter);

        // The dynamic truncation of RFC 4226: the low nibble of the last byte picks the four
        // bytes to read, and the top bit of the first of them is masked off.
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var modulus = (int)Math.Pow(10, Digits);
        return (binary % modulus).ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }

    /// <summary>
    /// Whether the code is the one valid at that instant, within a window of steps.
    /// </summary>
    /// <remarks>
    /// The window exists because the phone's clock and the server's clock disagree by a few
    /// seconds, and a code that is refused because of a 30-second boundary is a staff member
    /// locked out of the panel. The comparison itself is constant-time, and every step in the
    /// window is computed regardless of an earlier match, so the answer does not leak which
    /// step matched.
    /// </remarks>
    public static bool VerifyCode(byte[] secret, string code, DateTime utcNow, int window = DefaultWindow)
    {
        var normalized = Normalize(code);
        if (normalized is null)
            return false;

        var expected = Encoding.ASCII.GetBytes(normalized);
        var matched = false;

        for (var drift = -window; drift <= window; drift++)
        {
            var candidate = Encoding.ASCII.GetBytes(
                ComputeCode(secret, utcNow.AddSeconds(drift * PeriodSeconds)));
            matched |= CryptographicOperations.FixedTimeEquals(candidate, expected);
        }

        return matched;
    }

    /// <summary>
    /// The otpauth:// URI an authenticator app scans. The label is what the app shows next to
    /// the entry, so it carries the issuer and the account.
    /// </summary>
    public static string BuildProvisioningUri(string issuer, string accountLabel, string base32Secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(base32Secret);

        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedLabel = Uri.EscapeDataString(accountLabel ?? string.Empty);

        return $"otpauth://totp/{encodedIssuer}:{encodedLabel}"
               + $"?secret={base32Secret}"
               + $"&issuer={encodedIssuer}"
               + $"&algorithm=SHA1"
               + $"&digits={Digits}"
               + $"&period={PeriodSeconds}";
    }

    /// <summary>
    /// The code as a plain string of digits, or null when it cannot be one.
    /// </summary>
    private static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmed = code.Trim();
        if (trimmed.Length != Digits)
            return null;

        foreach (var c in trimmed)
        {
            if (!char.IsAsciiDigit(c))
                return null;
        }

        return trimmed;
    }

    private static byte[] ComputeHash(byte[] secret, long counter)
    {
        var counterBytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        using var hmac = new HMACSHA1(secret);
        return hmac.ComputeHash(counterBytes);
    }
}
