using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// Renders a voucher QR payload as a non-reversible fingerprint, for logs and stored error rows.
/// <para>
/// The payload is the bearer instrument: whoever holds it can have the voucher redeemed at
/// the pump, and FuelFlow cannot revoke it because redemption happens on the brand's side.
/// Writing it into <c>voucher_import_errors</c> and into the log stream copies redeemable
/// material into two stores with a wider audience and a longer retention than the voucher
/// table itself. Length plus a short digest is enough for an operator to tell two failing
/// rows apart and to confirm a re-import produced the same payload, without carrying the
/// payload anywhere.
/// </para>
/// <para>
/// It lives here, and not on the import command handler, because the decoder needs it too:
/// the decoder is the first thing in the process that holds a payload, and it used to write
/// it to the log in the clear - on every voucher of every import, at Information level,
/// into the console and on into Loki.
/// </para>
/// </summary>
internal static class QrPayloadFingerprint
{
    /// <summary>
    /// The payload's length and the first ten hex digits of its SHA-256. Never the payload.
    /// </summary>
    internal static string Describe(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return "<empty>";

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return $"<{payload.Length} chars, sha256:{Convert.ToHexString(digest)[..10].ToLowerInvariant()}>";
    }
}
