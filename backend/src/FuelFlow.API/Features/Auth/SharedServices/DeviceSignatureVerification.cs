using FuelFlow.SharedKernel.Security;

namespace FuelFlow.Features.Auth.SharedServices;

/// <summary>
/// The outcome of a device signature check: the scheme that matched, which payload shape was
/// signed, and every shape the verifier actually built.
/// </summary>
/// <remarks>
/// <para>
/// The payload version is a pin like the scheme, but a mismatch must not lock a device out. The pin
/// records the shape the client used when it enrolled; an OTA update can change that shape while
/// the row keeps the old value, and the only way to correct the row is a successful sign-in - which
/// the mismatch itself blocks. On 2026-10-28 exactly that happened in production: a bundle that
/// signs version 2 reached a fleet whose ten rows all pinned version 1, and every signed request
/// answered 401 with no path back for any of them.
/// </para>
/// <para>
/// Falling back is safe because the version describes the BYTES, not the signer: both shapes are
/// signed by the same private key, a signature made over one cannot be replayed as the other, and
/// the caller records whatever verified so the next request is checked against the shape the device
/// actually uses. The scheme pin keeps its zero-tolerance rule - a signature under a scheme the
/// device never enrolled with is still refused.
/// </para>
/// </remarks>
public sealed class DeviceSignatureVerification
{
    private readonly SignatureVerificationResult _result;

    public DeviceSignatureVerification(
        SignatureVerificationResult result,
        int? matchedPayloadVersion,
        IReadOnlyList<int> triedPayloadVersions)
    {
        _result = result;
        MatchedPayloadVersion = matchedPayloadVersion;
        TriedPayloadVersions = triedPayloadVersions;
    }

    /// <summary>The payload version that verified, or null when none did.</summary>
    public int? MatchedPayloadVersion { get; }

    /// <summary>The shapes the verifier built, in the order it built them.</summary>
    public IReadOnlyList<int> TriedPayloadVersions { get; }

    public bool IsValid => _result.IsValid;

    public AsymmetricSignatureAlgorithm? MatchedAlgorithm => _result.MatchedAlgorithm;

    public IReadOnlyList<SignatureAttempt> Attempts => _result.Attempts;

    public string? KeyType => _result.KeyType;

    public string? Failure => _result.Failure;
}
