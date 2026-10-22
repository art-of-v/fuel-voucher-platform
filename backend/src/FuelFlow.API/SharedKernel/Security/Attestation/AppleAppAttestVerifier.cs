using Fido2NetLib;
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
            var key = new AppAttestKey
            {
                KeyId = AttestationEncoding.DecodeBase64(keyId),
                PublicKey = AttestationEncoding.DecodeBase64(publicKey),
                Counter = (uint)counter,
                Environment = attestationEnvironment,
            };

            var result = GetVerifier(_options.CurrentValue, attestationEnvironment)
                .VerifyAssertion(assertion, clientDataHash, key);

            return new AppleAssertionData(result.Counter);
        }
        catch (AttestationVerificationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AttestationVerificationException(
                "APPLE_ASSERTION_INVALID",
                "The App Attest assertion could not be verified.",
                exception);
        }
    }

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
