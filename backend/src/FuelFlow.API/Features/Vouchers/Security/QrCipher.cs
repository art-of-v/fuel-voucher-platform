using System.Security.Cryptography;
using System.Text;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Vouchers.Security;

/// <summary>
/// Encrypts and decrypts a voucher QR payload, and fingerprints it for deduplication.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not pgcrypto.</b> D-20 permits it and it was the first design, but encrypting in the
/// database means the key has to reach PostgreSQL - as a session GUC or in the connection string.
/// Either way it becomes something the database can log: <c>pg_stat_activity</c>, a connection
/// string in a crash dump, a proxy in between. It also forces the EF value converter to build SQL,
/// and a converter that interpolates a plaintext value into a statement is an injection surface
/// that does not exist anywhere else in this codebase.
/// </para>
/// <para>
/// AES-GCM in the process keeps the key in memory, makes the whole thing one round trip instead of
/// two, and leaves nothing to inject. The cost is that a backup of the database alone cannot be
/// read without the key - which is the point of encrypting it, and is why <b>D-22's RPO decision
/// must be read together with this one</b>: losing the key loses the vouchers, permanently.
/// </para>
/// <para>
/// <b>Layout.</b> One column, concatenated: <c>[12-byte nonce][16-byte tag][ciphertext]</c>. GCM needs
/// a unique nonce per encryption under the same key, so it is random per call rather than derived -
/// which is also why the ciphertext cannot be searched, and why
/// <see cref="QrPayloadSha256"/> exists.
/// </para>
/// </remarks>
public interface IQrCipher
{
    /// <summary>Encrypts for storage. Returns nonce, tag and ciphertext concatenated.</summary>
    byte[] Encrypt(string plaintext);

    /// <summary>Recovers the plaintext. Throws if the key is wrong or the data was tampered with.</summary>
    string Decrypt(byte[] stored);

    /// <summary>The plaintext's fingerprint, for the uniqueness index. Not reversible.</summary>
    byte[] Sha256(string plaintext);
}

public sealed class QrCipher : IQrCipher
{
    /// <summary>AES-GCM standard nonce length. 96 bits, the size GCM is specified for.</summary>
    private const int NonceSize = 12;

    /// <summary>GCM's full-strength authentication tag.</summary>
    private const int TagSize = 16;

    /// <summary>AES-256.</summary>
    private const int KeySize = 32;

    private readonly byte[] _key;

    public QrCipher(IOptions<QrCipherOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.Value.Key;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "QrCipher:Key is not configured. A voucher QR payload cannot be stored without it, and " +
                "the app must not invent one: a key that defaults is a key that everyone has.");
        }

        _key = Decode(configured);
    }

/// <summary>
    /// Accepts base64 or hex, and rejects anything that is not exactly 32 bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both forms are tried and the <em>length</em> decides, rather than trying base64 first and only
    /// falling back to hex on a parse failure. That ordering is a trap: a 32-byte key in hex is 64
    /// characters, and 64 is a valid base64 length consisting entirely of valid base64 characters, so
    /// a hex key parses as base64 into 48 bytes and the operator is told the key is the wrong length
    /// for a key that is exactly right.
    /// </para>
    /// <para>
    /// The length check is the point either way. A truncated key does not fail loudly - AES would
    /// happily pad or reject per block - and a QR payload encrypted under a key nobody can reproduce
    /// is a voucher that can never be verified. Failing at startup is the only cheap moment to notice.
    /// </para>
    /// </remarks>
private static byte[] Decode(string key)
    {
        var attempts = new List<(string Form, byte[] Bytes)>(2);

        if (TryBase64(key, out var fromBase64))
        {
            attempts.Add(("base64", fromBase64));
        }

        if (TryHex(key, out var fromHex))
        {
            attempts.Add(("hex", fromHex));
        }

        var exact = attempts.Where(a => a.Bytes.Length == KeySize).ToList();

        if (exact.Count == 1)
        {
            return exact[0].Bytes;
        }

        if (exact.Count > 1)
        {
            // Only reachable if a 32-byte value is simultaneously valid base64 and valid hex, which
            // would mean the same bytes - so either answer is the same key and this cannot happen.
            // Kept as a refusal rather than an arbitrary pick.
            throw new InvalidOperationException(
                "QrCipher:Key is ambiguous between base64 and hex.");
        }

        if (attempts.Count == 0)
        {
            throw new InvalidOperationException(
                "QrCipher:Key must be base64 or hex encoded.");
        }

        throw new InvalidOperationException(
            $"QrCipher:Key must decode to exactly {KeySize} bytes for AES-256; the {attempts[0].Form} " +
            $"form gave {attempts[0].Bytes.Length}. Generate one with: openssl rand -base64 32");
    }

    private static bool TryBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool TryHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    public byte[] Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[NonceSize + TagSize + plainBytes.Length];

        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        var tag = result.AsSpan(NonceSize, TagSize);
        var cipher = result.AsSpan(NonceSize + TagSize, plainBytes.Length);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return result;
    }

    public string Decrypt(byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.Length < NonceSize + TagSize)
        {
            throw new CryptographicException(
                "Stored QR payload is too short to contain a nonce and tag; the column has been " +
                "written by something other than this cipher.");
        }

        ReadOnlySpan<byte> nonce = stored.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> tag = stored.AsSpan(NonceSize, TagSize);
        ReadOnlySpan<byte> cipher = stored.AsSpan(NonceSize + TagSize);

        var plainBytes = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    public byte[] Sha256(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
    }
}