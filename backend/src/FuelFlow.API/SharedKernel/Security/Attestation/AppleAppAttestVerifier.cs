using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Fido2NetLib;
using Fido2NetLib.Cbor;
using Fido2NetLib.Objects;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.SharedKernel.Security.Attestation;

/// <summary>
/// Wraps Fido2NetLib's App Attest verifier. Fido2 is pinned to 5.0.0-preview.8 because that is the
/// first version that carries the App Attest types at all; the stable 4.x package exposes only the
/// root certificate. Re-implementing the CBOR and certificate-chain checks by hand would be a
/// larger and less-reviewed security surface than taking the preview package and pinning it.
/// </summary>
public sealed class AppleAppAttestVerifier : IAppleAppAttestVerifier
{
    private readonly IOptionsMonitor<AttestationOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AppleAppAttestVerifier> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<(string AppId, AppAttestEnvironment Environment), AppAttest> _verifiers = new();

    public AppleAppAttestVerifier(
        IOptionsMonitor<AttestationOptions> options,
        IHostEnvironment environment,
        ILogger<AppleAppAttestVerifier> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public async Task<AppleAttestationData> VerifyAttestationAsync(
        byte[] attestationObject,
        byte[] keyId,
        byte[] clientDataHash,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var primary = ParseEnvironment(options.AppleEnvironment);
        var candidates = new List<AppAttestEnvironment> { primary };

        // Outside Production a developer may be running the app from Xcode, whose attestation is
        // a Development one. Production accepts only the configured environment.
        if (!_environment.IsProduction() && primary != AppAttestEnvironment.Development)
            candidates.Add(AppAttestEnvironment.Development);

        Exception? lastFailure = null;

        foreach (var environment in candidates)
        {
            try
            {
                var result = await GetVerifier(options, environment)
                    .VerifyAttestationAsync(attestationObject, keyId, clientDataHash);

                var key = result.Key;

                return new AppleAttestationData(
                    AttestationEncoding.ToBase64(key.KeyId),
                    AttestationEncoding.ToBase64(key.PublicKey),
                    key.Counter,
                    key.Environment.ToString(),
                    key.Receipt is { Length: > 0 } ? AttestationEncoding.ToBase64(key.Receipt) : null);
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }
        }

        _logger.LogWarning(
            lastFailure,
            "App Attest attestation verification failed for key {KeyIdPrefix}",
            Convert.ToBase64String(keyId)[..Math.Min(8, Convert.ToBase64String(keyId).Length)]);

        throw new AttestationVerificationException(
            "APPLE_ATTESTATION_INVALID",
            "The App Attest attestation could not be verified.",
            lastFailure);
    }

    public AppleAssertionData VerifyAssertion(
        byte[] assertion,
        byte[] clientDataHash,
        string keyId,
        string publicKey,
        long counter,
        string environment)
    {
        if (counter is < 0 or > uint.MaxValue)
        {
            throw new AttestationVerificationException(
                "APPLE_ASSERTION_INVALID",
                "The stored assertion counter is outside the range App Attest uses.");
        }

        var attestationEnvironment = ParseEnvironment(environment);

        if (_environment.IsProduction() && attestationEnvironment == AppAttestEnvironment.Development)
        {
            throw new AttestationVerificationException(
                "APPLE_KEY_ENVIRONMENT_INVALID",
                "A Development App Attest key cannot be used in Production.");
        }

        try
        {
            return VerifyAssertionCore(assertion, clientDataHash, publicKey, counter, keyId);
        }
        catch (AttestationVerificationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "App Attest assertion verification failed unexpectedly for key {KeyIdPrefix}",
                Base64Prefix(keyId));

            throw new AttestationVerificationException(
                "APPLE_ASSERTION_INVALID",
                "The App Attest assertion could not be verified.",
                exception);
        }
    }

    /// <summary>
    /// Apple's assertion is a CBOR map of a signature and the authenticator data that signature
    /// covers. Since iOS 27 that data carries a WebAuthn-style extensions map after the counter
    /// while leaving the ED flag clear, and Fido2's WebAuthn-shaped parser reads the extensions as
    /// an attested credential and refuses every assertion. The fixed App Attest layout is parsed
    /// here instead; the signature, the RP ID hash and the counter are still checked against the
    /// stored key.
    /// </summary>
    private AppleAssertionData VerifyAssertionCore(
        byte[] assertion,
        byte[] clientDataHash,
        string publicKey,
        long storedCounter,
        string keyId)
    {
        CborObject decoded;

        try
        {
            decoded = CborObject.Decode(assertion);
        }
        catch (Exception exception)
        {
            throw Refuse(keyId, "assertion is not well-formed CBOR", exception);
        }

        if (decoded is not CborMap map)
            throw Refuse(keyId, "assertion is not a CBOR map");

        if (map["signature"] is not CborByteString { Length: > 0 } signature
            || map["authenticatorData"] is not CborByteString { Length: > 0 } authenticatorData)
            throw Refuse(keyId, "assertion has no signature or authenticator data");

        var data = authenticatorData.Value;

        if (data.Length < 37)
            throw Refuse(keyId, $"authenticator data is {data.Length} bytes, shorter than App Attest's 37-byte layout");

        if (data.Length > 37 && !IsSingleCborMap(data.AsMemory(37)))
            throw Refuse(keyId, "bytes after the counter are not one CBOR extensions map");

        var nonce = SHA256.HashData([.. data, .. clientDataHash]);
        var key = new CredentialPublicKey(AttestationEncoding.DecodeBase64(publicKey));

        if (!key.Verify(nonce, signature.Value))
            throw Refuse(keyId, "signature does not verify under the stored key");

        var appIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(_options.CurrentValue.AppleAppId));

        if (!data.AsSpan(0, 32).SequenceEqual(appIdHash))
            throw Refuse(keyId, "assertion is for another app");

        var counter = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(33, 4));

        if (counter <= storedCounter)
            throw Refuse(keyId, $"counter {counter} did not advance past {storedCounter}");

        return new AppleAssertionData(counter);
    }

    private static bool IsSingleCborMap(ReadOnlyMemory<byte> value)
    {
        try
        {
            return CborObject.Decode(value, out var bytesRead) is CborMap
                && bytesRead == value.Length;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private AttestationVerificationException Refuse(string keyId, string reason, Exception? inner = null)
    {
        _logger.LogWarning(
            inner,
            "App Attest assertion refused: {Reason} (key {KeyIdPrefix})",
            reason,
            Base64Prefix(keyId));

        return new AttestationVerificationException(
            "APPLE_ASSERTION_INVALID",
            "The App Attest assertion could not be verified.",
            inner);
    }

    private static string Base64Prefix(string keyId) =>
        keyId[..Math.Min(8, keyId.Length)];

    private AppAttest GetVerifier(AttestationOptions options, AppAttestEnvironment environment)
    {
        lock (_gate)
        {
            var cacheKey = (options.AppleAppId, environment);

            if (_verifiers.TryGetValue(cacheKey, out var existing))
                return existing;

            var verifier = new AppAttest(new AppAttestConfiguration
            {
                AppId = options.AppleAppId,
                Environments = environment,
            });

            _verifiers[cacheKey] = verifier;
            return verifier;
        }
    }

    private static AppAttestEnvironment ParseEnvironment(string? value) =>
        string.Equals(value, nameof(AppAttestEnvironment.Development), StringComparison.OrdinalIgnoreCase)
            ? AppAttestEnvironment.Development
            : AppAttestEnvironment.Production;
}
