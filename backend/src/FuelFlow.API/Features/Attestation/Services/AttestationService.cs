using System.Security.Cryptography;
using FuelFlow.Features.Attestation.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Security.Attestation;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Attestation;

public sealed record AttestationProof(
    string Provider,
    string Challenge,
    string Proof,
    string? KeyId);

public sealed record AppleRegistrationOutcome(
    AttestationCheckResult Result,
    bool AlreadyRegistered = false);

/// <summary>
/// Issues the two attestation decisions the API needs: register an Apple key once, and verify the
/// proof on every protected request.
/// </summary>
/// <remarks>
/// The per-provider cryptography lives behind <see cref="IAppleAppAttestVerifier"/> and
/// <see cref="IPlayIntegrityVerifier"/>. This class owns the parts that are easy to get wrong in
/// either provider: burning the challenge exactly once, binding it to the request, finding the
/// key by its server-stored id, and advancing the App Attest counter atomically.
/// </remarks>
public sealed class AttestationService
{
    private readonly ApplicationDbContext _db;
    private readonly AttestationChallengeService _challenges;
    private readonly IAppleAppAttestVerifier _apple;
    private readonly IPlayIntegrityVerifier _google;
    private readonly TimeProvider _time;
    private readonly ILogger<AttestationService> _logger;

    public AttestationService(
        ApplicationDbContext db,
        AttestationChallengeService challenges,
        IAppleAppAttestVerifier apple,
        IPlayIntegrityVerifier google,
        TimeProvider time,
        ILogger<AttestationService> logger)
    {
        _db = db;
        _challenges = challenges;
        _apple = apple;
        _google = google;
        _time = time;
        _logger = logger;
    }

    public async Task<AppleRegistrationOutcome> RegisterAppleKeyAsync(
        string deviceId,
        string keyId,
        string attestationObject,
        string challenge,
        CancellationToken cancellationToken)
    {
        byte[] keyIdBytes;
        byte[] attestationBytes;

        try
        {
            keyIdBytes = AttestationEncoding.DecodeBase64(keyId);
            attestationBytes = AttestationEncoding.DecodeBase64(attestationObject);
        }
        catch (FormatException)
        {
            return InvalidRegistration(
                "ATTESTATION_INVALID",
                "The App Attest key or attestation object is not valid base64.");
        }

        if (keyIdBytes.Length != 32 || attestationBytes.Length == 0)
        {
            return InvalidRegistration(
                "ATTESTATION_INVALID",
                "The App Attest key or attestation object has the wrong length.");
        }

        var validation = await _challenges.ConsumeAsync(
            challenge,
            deviceId,
            AttestationChallengeService.AppleAttestAction,
            expectedRequestHash: null,
            cancellationToken);

        if (!validation.IsValid)
        {
            return InvalidRegistration(
                "ATTESTATION_CHALLENGE_INVALID",
                "The attestation challenge is invalid or has expired. Try again.");
        }

        AppleAttestationData data;

        try
        {
            data = await _apple.VerifyAttestationAsync(
                attestationBytes,
                keyIdBytes,
                AttestationEncoding.Sha256(challenge),
                cancellationToken);
        }
        catch (AttestationVerificationException exception)
        {
            return InvalidRegistration(exception.Code, exception.Message);
        }

        var serverKeyId = AttestationEncoding.DecodeBase64(data.KeyId);

        if (!CryptographicOperations.FixedTimeEquals(serverKeyId, keyIdBytes))
        {
            return InvalidRegistration(
                "APPLE_ATTESTATION_INVALID",
                "The attestation's key id does not match the key that was registered.");
        }

        var keyIdBase64 = AttestationEncoding.ToBase64(serverKeyId);
        var existing = await _db.AttestationKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(key => key.KeyId == keyIdBase64, cancellationToken);

        if (existing is not null)
            return ExistingKeyOutcome(existing, deviceId);

        var now = _time.GetUtcNow().UtcDateTime;

        var entity = new AttestationKey
        {
            KeyId = keyIdBase64,
            DeviceId = deviceId,
            PublicKey = data.PublicKey,
            Counter = data.Counter,
            Environment = data.Environment,
            Receipt = data.Receipt,
            CreatedAt = now,
            LastUsedAt = now,
        };

        _db.AttestationKeys.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two devices can race to register the same key. The unique index is the arbiter; read
            // the row it protected and answer with the same semantics as the non-racing path.
            _db.ChangeTracker.Clear();

            var raced = await _db.AttestationKeys
                .AsNoTracking()
                .SingleOrDefaultAsync(key => key.KeyId == keyIdBase64, cancellationToken);

            if (raced is null)
                throw;

            _logger.LogInformation(exception, "Concurrent App Attest key registration resolved from the unique index");

            return ExistingKeyOutcome(raced, deviceId);
        }

        _logger.LogInformation(
            "Registered App Attest key {KeyIdPrefix} for device {DevicePrefix}",
            Short(keyIdBase64),
            Short(deviceId));

        return new AppleRegistrationOutcome(AttestationCheckResult.Valid());
    }

    public async Task<AttestationCheckResult> VerifyAsync(
        string method,
        string path,
        string body,
        string deviceId,
        string? clientOs,
        AttestationProof proof,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proof.Challenge)
            || string.IsNullOrWhiteSpace(proof.Proof))
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_INVALID",
                "The attestation headers are incomplete.");
        }

        if (proof.Challenge.Length > 128 || proof.Proof.Length > 64_000)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_INVALID",
                "The attestation headers are too large.");
        }

        if (string.IsNullOrWhiteSpace(proof.Provider))
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_PROVIDER_INVALID",
                "The attestation provider is missing.");
        }

        var provider = proof.Provider.Trim().ToLowerInvariant();
        var expectedProvider = clientOs?.Trim().ToLowerInvariant() switch
        {
            "ios" => "apple",
            "android" => "google",
            _ => null,
        };

        if (expectedProvider is not null && provider != expectedProvider)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_PROVIDER_MISMATCH",
                "The attestation provider does not match the client platform.");
        }

        var requestHash = AttestationRequestHasher.Compute(method, path, body);

        return provider switch
        {
            "apple" => await VerifyAppleAsync(
                deviceId, proof, requestHash, cancellationToken),
            "google" => await VerifyGoogleAsync(
                deviceId, proof, requestHash, cancellationToken),
            _ => AttestationCheckResult.Invalid(
                "ATTESTATION_PROVIDER_INVALID",
                "The attestation provider is not supported."),
        };
    }

    private async Task<AttestationCheckResult> VerifyAppleAsync(
        string deviceId,
        AttestationProof proof,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proof.KeyId))
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_INVALID",
                "The App Attest key id is missing.");
        }

        byte[] keyIdBytes;
        byte[] assertionBytes;

        try
        {
            keyIdBytes = AttestationEncoding.DecodeBase64(proof.KeyId);
            assertionBytes = AttestationEncoding.DecodeBase64(proof.Proof);
        }
        catch (FormatException)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_INVALID",
                "The App Attest proof is not valid base64.");
        }

        if (keyIdBytes.Length != 32 || assertionBytes.Length == 0)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_INVALID",
                "The App Attest proof has the wrong length.");
        }

        var keyIdBase64 = AttestationEncoding.ToBase64(keyIdBytes);

        var validation = await _challenges.ConsumeAsync(
            proof.Challenge,
            deviceId,
            AttestationChallengeService.MobileRequestAction,
            requestHash,
            cancellationToken);

        if (!validation.IsValid)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_CHALLENGE_INVALID",
                "The attestation challenge is invalid or has expired. Try again.");
        }

        var key = await _db.AttestationKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.KeyId == keyIdBase64, cancellationToken);

        if (key is null || !string.Equals(key.DeviceId, deviceId, StringComparison.Ordinal))
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_KEY_UNKNOWN",
                "The App Attest key is not registered for this device.");
        }

        if (key.RevokedAt is not null)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_KEY_REVOKED",
                "The App Attest key has been revoked.");
        }

        AppleAssertionData assertion;

        try
        {
            assertion = _apple.VerifyAssertion(
                assertionBytes,
                AttestationRequestHasher.ComputeClientDataHash(
                    validation.Entry!.Challenge,
                    validation.Entry.RequestHash),
                key.KeyId,
                key.PublicKey,
                key.Counter,
                key.Environment);
        }
        catch (AttestationVerificationException exception)
        {
            return AttestationCheckResult.Invalid(exception.Code, exception.Message);
        }

        if (assertion.Counter <= key.Counter)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_REPLAY",
                "The App Attest assertion counter did not advance.");
        }

        var now = _time.GetUtcNow().UtcDateTime;

        var updated = await _db.AttestationKeys
            .Where(item => item.Id == key.Id
                && item.RevokedAt == null
                && item.Counter < assertion.Counter)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Counter, assertion.Counter)
                    .SetProperty(item => item.LastUsedAt, now),
                cancellationToken);

        if (updated != 1)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_REPLAY",
                "The App Attest assertion counter did not advance.");
        }

        return AttestationCheckResult.Valid();
    }

    private async Task<AttestationCheckResult> VerifyGoogleAsync(
        string deviceId,
        AttestationProof proof,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var validation = await _challenges.ConsumeAsync(
            proof.Challenge,
            deviceId,
            AttestationChallengeService.MobileRequestAction,
            requestHash,
            cancellationToken);

        if (!validation.IsValid)
        {
            return AttestationCheckResult.Invalid(
                "ATTESTATION_CHALLENGE_INVALID",
                "The attestation challenge is invalid or has expired. Try again.");
        }

        PlayIntegrityResult result;

        try
        {
            result = await _google.VerifyAsync(
                proof.Proof,
                AttestationRequestHasher.ComputeProofRequestHash(
                    validation.Entry!.Challenge,
                    validation.Entry.RequestHash),
                cancellationToken);
        }
        catch (AttestationVerificationException exception)
        {
            return AttestationCheckResult.Unavailable(exception.Message);
        }

        if (!result.IsValid)
        {
            _logger.LogWarning(
                "Play Integrity rejected a request: {Code} app={AppVerdict} device={DeviceVerdict} testing={Testing}",
                result.Code,
                result.AppVerdict,
                result.DeviceVerdict,
                result.IsTestingResponse);

            return AttestationCheckResult.Invalid(
                result.Code ?? "GOOGLE_TOKEN_INVALID",
                result.Detail ?? "The Play Integrity proof was rejected.");
        }

        return AttestationCheckResult.Valid();
    }

    private static AppleRegistrationOutcome ExistingKeyOutcome(
        AttestationKey existing,
        string deviceId)
    {
        if (existing.RevokedAt is not null)
        {
            return InvalidRegistration(
                "ATTESTATION_KEY_REVOKED",
                "This App Attest key has been revoked.");
        }

        if (!string.Equals(existing.DeviceId, deviceId, StringComparison.Ordinal))
        {
            return InvalidRegistration(
                "ATTESTATION_KEY_EXISTS",
                "This App Attest key is registered to another device.");
        }

        return new AppleRegistrationOutcome(AttestationCheckResult.Valid(), AlreadyRegistered: true);
    }

    private static AppleRegistrationOutcome InvalidRegistration(string code, string detail) =>
        new(AttestationCheckResult.Invalid(code, detail));

    private static string Short(string value) =>
        value.Length <= 8 ? value : value[..8];
}
