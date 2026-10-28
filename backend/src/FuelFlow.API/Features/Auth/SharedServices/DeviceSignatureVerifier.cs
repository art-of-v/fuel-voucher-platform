using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Security;

namespace FuelFlow.Features.Auth.SharedServices;

/// <summary>
/// The one place a device signature is verified: the login challenge and the signed checkout
/// request both come through here, and the <c>verify-raw</c> diagnostic asks the same verifier
/// what it thinks. The crypto stays in <see cref="IAsymmetricSignatureVerifier"/>; this class
/// decides WHICH scheme is allowed and WHICH bytes the device signed.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed the challenge handler and the diagnostic endpoint each carried their own
/// RSA-then-PSS-then-ECDSA fallback, 100 lines of duplicated crypto that could disagree. The
/// device row also recorded only a public key, so nothing bound a key to the scheme its owner
/// uses: a signature that only verified under PSS was accepted exactly as readily as the PKCS#1
/// one the shipped app produces.
/// </para>
/// <para>
/// A device now carries two pins: <see cref="Device.SignatureAlgorithm"/> and
/// <see cref="Device.SignaturePayloadVersion"/>. The first is the scheme; the second is the shape
/// of the bytes that were signed. Both are nullable/defaulted so rows enrolled before the pins
/// existed keep working: a null algorithm is tried against every scheme once and is pinned to
/// whatever worked, and a null version means version 1.
/// </para>
/// </remarks>
public sealed class DeviceSignatureVerifier
{
    /// <summary>
    /// The first payload shape, kept byte-for-byte for the installed base: the raw challenge for
    /// login, and METHOD + path + body + timestamp for a request.
    /// </summary>
    public const int Version1 = 1;

    /// <summary>
    /// A domain-separated, delimiter-safe payload. The prefix names the protocol and the context
    /// (<c>challenge</c> or <c>request</c>), so a signature made for one purpose can never be
    /// presented as the other even if the two strings were to collide.
    /// </summary>
    public const int Version2 = 2;

    public const string Version2Prefix = "FuelFlow-DeviceSignature-v2";

    private static readonly IReadOnlyCollection<AsymmetricSignatureAlgorithm> AllAlgorithms =
    [
        AsymmetricSignatureAlgorithm.RsaPkcs1Sha256,
        AsymmetricSignatureAlgorithm.RsaPssSha256,
        AsymmetricSignatureAlgorithm.EcdsaSha256Der,
        AsymmetricSignatureAlgorithm.EcdsaSha256P1363
    ];

    private readonly IAsymmetricSignatureVerifier _verifier;

    public DeviceSignatureVerifier(IAsymmetricSignatureVerifier verifier) => _verifier = verifier;

    public static bool IsSupportedVersion(int version) => version is Version1 or Version2;

    /// <summary>
    /// The bytes a device signs to prove possession of its key.
    /// </summary>
    /// <remarks>
    /// Version 2 is built with <c>\n</c> between every field and no trailing delimiter, so the
    /// fields cannot be shifted across a boundary. The device id is included in both contexts:
    /// a challenge issued to one device must not be answerable by another, even though the
    /// challenge value is already unique.
    /// </remarks>
    public static string BuildChallengePayload(string deviceId, string challenge, int version) =>
        version switch
        {
            Version1 => challenge,
            Version2 => $"{Version2Prefix}\nchallenge\n{deviceId}\n{challenge}",
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported payload version.")
        };

    public static string BuildRequestPayload(
        string deviceId,
        string method,
        string path,
        string body,
        string timestamp,
        int version) =>
        version switch
        {
            Version1 => $"{method}{path}{body}{timestamp}",
            Version2 => $"{Version2Prefix}\nrequest\n{deviceId}\n{method}\n{path}\n{timestamp}\n{body}",
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported payload version.")
        };

    /// <summary>
    /// Resolves what a registration asked to pin. An absent scheme is inferred from the key: RSA
    /// defaults to PKCS#1 (what the shipped app produces), EC to DER (what every mainstream stack
    /// emits). An explicit scheme that does not belong to the key's family is refused rather than
    /// stored, because it could never verify and would lock the device out on its next login.
    /// </summary>
    public static bool TryResolveAlgorithm(
        string? requested,
        string publicKey,
        out AsymmetricSignatureAlgorithm algorithm,
        out string error)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!Enum.TryParse<AsymmetricSignatureAlgorithm>(requested, ignoreCase: true, out algorithm)
                || !Enum.IsDefined(algorithm))
            {
                error = "UnsupportedSignatureAlgorithm";
                return false;
            }

            if (!AsymmetricSignatureVerifier.AlgorithmMatchesKey(algorithm, publicKey))
            {
                error = "SignatureAlgorithmDoesNotMatchKey";
                return false;
            }

            error = string.Empty;
            return true;
        }

        var keyType = AsymmetricSignatureVerifier.DetectKeyType(publicKey);
        switch (keyType)
        {
            case "RSA":
                algorithm = AsymmetricSignatureAlgorithm.RsaPkcs1Sha256;
                error = string.Empty;
                return true;
            case "ECDSA":
                algorithm = AsymmetricSignatureAlgorithm.EcdsaSha256Der;
                error = string.Empty;
                return true;
            default:
                algorithm = default;
                error = "InvalidPublicKey";
                return false;
        }
    }

    /// <summary>
    /// Verifies the challenge signature. <paramref name="device"/> supplies both pins; a legacy
    /// row with no algorithm is tried against every scheme so the first successful login can pin
    /// it, which is the migration path for devices enrolled before the pins existed.
    /// </summary>
    public DeviceSignatureVerification VerifyChallenge(
        Device device,
        string deviceId,
        string challenge,
        string signature) =>
        VerifyAcrossVersions(
            device,
            version => BuildChallengePayload(deviceId, challenge, version),
            signature);

    /// <summary>
    /// Verifies a signed request. The caller passes the same method/path/body/timestamp the
    /// middleware puts into the payload.
    /// </summary>
    public DeviceSignatureVerification VerifyRequest(
        Device device,
        string method,
        string path,
        string body,
        string timestamp,
        string signature) =>
        VerifyAcrossVersions(
            device,
            version => BuildRequestPayload(device.DeviceId, method, path, body, timestamp, version),
            signature);

    /// <summary>
    /// The diagnostic path for a key that is not enrolled yet: tries every scheme and reports
    /// which one, if any, verifies. It deliberately runs the production verifier rather than a
    /// second copy of it, so the diagnostic cannot say "valid" for bytes the login path rejects.
    /// </summary>
    public SignatureVerificationResult Diagnose(string payload, string signature, string publicKey) =>
        _verifier.VerifyWith(payload, signature, publicKey, AllAlgorithms);

    /// <summary>
    /// Verifies the pinned payload version first, then the other supported one.
    /// </summary>
    /// <remarks>
    /// Only the SHAPE may fall back. The scheme is still tried once, exactly as pinned: a
    /// signature under a scheme the device never enrolled with is still refused, because that is
    /// evidence of a different signer. A version mismatch is not - it is the client and its row
    /// having drifted apart, which an OTA update does on its own. The caller records the version
    /// that verified, so the second attempt happens once per device rather than once per request.
    /// </remarks>
    private DeviceSignatureVerification VerifyAcrossVersions(
        Device device,
        Func<int, string> buildPayload,
        string signature)
    {
        var pinnedVersion = device.SignaturePayloadVersion;
        var pinnedResult = Verify(device, buildPayload(pinnedVersion), signature);

        if (pinnedResult.IsValid)
        {
            return new DeviceSignatureVerification(pinnedResult, pinnedVersion, [pinnedVersion]);
        }

        // A malformed signature or an unreadable key fails before the payload is involved, so the
        // other shape cannot change the answer.
        if (pinnedResult.Failure is not null)
        {
            return new DeviceSignatureVerification(pinnedResult, null, [pinnedVersion]);
        }

        var otherVersion = pinnedVersion == Version2 ? Version1 : Version2;
        var otherResult = Verify(device, buildPayload(otherVersion), signature);

        return otherResult.IsValid
            ? new DeviceSignatureVerification(otherResult, otherVersion, [pinnedVersion, otherVersion])
            : new DeviceSignatureVerification(pinnedResult, null, [pinnedVersion, otherVersion]);
    }

    private SignatureVerificationResult Verify(Device device, string payload, string signature)
    {
        var allowed = device.SignatureAlgorithm is { } pinned
            ? new[] { pinned }
            : AllAlgorithms;

        return _verifier.VerifyWith(payload, signature, device.PublicKey, allowed);
    }
}
