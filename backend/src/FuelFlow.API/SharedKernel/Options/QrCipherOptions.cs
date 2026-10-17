namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// The key that encrypts voucher QR payloads at rest.
/// </summary>
/// <remarks>
/// <para>
/// Bound from configuration, never defaulted, and never committed. The app refuses to start without
/// it - see <c>QrCipher</c> - because a default key is a key every deployment shares, and QR payloads
/// are what a verifier trusts.
/// </para>
/// <para>
/// <b>Losing this key loses every voucher QR permanently.</b> There is no recovery path in the
/// database, because the database no longer holds anything it can be decrypted with. Pair any
/// rotation or backup policy with that fact; see D-22's RPO note, which this makes concrete.
/// </para>
/// </remarks>
public sealed class QrCipherOptions
{
    public const string SectionName = "QrCipher";

    /// <summary>
    /// Base64 or hex, decoding to exactly 32 bytes. Generate with <c>openssl rand -base64 32</c>.
    /// Supplied through the environment in production, never from a committed file.
    /// </summary>
    public string Key { get; set; } = string.Empty;
}