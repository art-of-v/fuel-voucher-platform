using System.Text.Json.Serialization;

namespace FuelFlow.Features.Updates;

/// <summary>
/// The pre-signed update record the Mac publish script (mobile/scripts/publish-ota.mjs) writes to R2
/// at <c>{platform}/{runtimeVersion}/update.json</c>. The API relays its contents verbatim and never
/// signs anything itself — that is what keeps the private signing key off every server. A stolen R2
/// token or a compromised API can therefore only serve manifests that were already signed on the
/// publish machine; anything else fails the client's signature check and is rejected.
/// </summary>
public sealed class UpdateDescriptor
{
    /// <summary>Base64 of the exact manifest JSON bytes that were signed on the publish machine.</summary>
    [JsonPropertyName("manifestBase64")]
    public string ManifestBase64 { get; init; } = string.Empty;

    /// <summary>
    /// The full <c>expo-signature</c> structured-field value, ready to drop onto the manifest part
    /// header — e.g. <c>sig="…", keyid="main", alg="rsa-v1_5-sha256"</c>. Produced by the publish
    /// machine so the API stays a dumb pass-through.
    /// </summary>
    [JsonPropertyName("signature")]
    public string Signature { get; init; } = string.Empty;
}
