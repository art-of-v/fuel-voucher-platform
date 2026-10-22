using System.Security.Cryptography;
using System.Text.Json;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security.Attestation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Attestation;

public enum ChallengeStatus
{
    Valid = 0,
    Missing = 1,
    Expired = 2,
    DeviceMismatch = 3,
    ActionMismatch = 4,
    RequestMismatch = 5,
    Malformed = 6,
}

public sealed record IssuedChallenge(string Challenge, DateTimeOffset ExpiresAt);

/// <summary>
/// The challenge the server stores, and the fields that make it usable exactly once, by exactly
/// one device, for exactly one request.
/// </summary>
public sealed record StoredChallenge(
    string Challenge,
    string DeviceId,
    string Action,
    string RequestHash,
    DateTimeOffset ExpiresAt);

public sealed record ChallengeValidation(ChallengeStatus Status, StoredChallenge? Entry = null)
{
    public bool IsValid => Status == ChallengeStatus.Valid;
}

/// <summary>
/// Issues one-time attestation challenges and consumes them atomically.
/// </summary>
/// <remarks>
/// <para>
/// The challenge is the replay defence. It is random, short-lived and removed by the same cache
/// operation that reads it, so two concurrent proofs carrying the same challenge cannot both
/// succeed. Consuming on a device or action mismatch is intentional: a caller who tries to use
/// somebody else's challenge has burned it rather than keeping it alive for another guess.
/// </para>
/// <para>
/// A challenge is bound to the device that asked for it, not to a user, because the mobile
/// auth endpoints run before there is a user token. The device id is the same one the existing
/// device-signature middleware uses.
/// </para>
/// </remarks>
public sealed class AttestationChallengeService
{
    public const string MobileRequestAction = "mobile-request";
    public const string AppleAttestAction = "apple-attest";

    private const string CacheKeyPrefix = "attestation:challenge:";

    private readonly ICacheService _cache;
    private readonly IOptionsMonitor<AttestationOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AttestationChallengeService> _logger;

    public AttestationChallengeService(
        ICacheService cache,
        IOptionsMonitor<AttestationOptions> options,
        TimeProvider time,
        ILogger<AttestationChallengeService> logger)
    {
        _cache = cache;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task<IssuedChallenge> IssueAsync(
        string deviceId,
        string action,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var challenge = AttestationEncoding.ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var ttl = Math.Clamp(_options.CurrentValue.ChallengeExpirySeconds, 30, 600);
        var expiresAt = _time.GetUtcNow().AddSeconds(ttl);

        var entry = new StoredChallenge(challenge, deviceId, action, requestHash, expiresAt);

        try
        {
            await _cache.SetAsync(
                CacheKeyPrefix + challenge,
                JsonSerializer.Serialize(entry),
                TimeSpan.FromSeconds(ttl),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AttestationVerificationException(
                "ATTESTATION_UNAVAILABLE",
                "The attestation challenge store is unavailable.",
                exception);
        }

        return new IssuedChallenge(challenge, expiresAt);
    }

    public async Task<ChallengeValidation> ConsumeAsync(
        string challenge,
        string deviceId,
        string expectedAction,
        string? expectedRequestHash,
        CancellationToken cancellationToken)
    {
        string? stored;

        try
        {
            stored = await _cache.GetAndDeleteAsync(CacheKeyPrefix + challenge, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AttestationVerificationException(
                "ATTESTATION_UNAVAILABLE",
                "The attestation challenge store is unavailable.",
                exception);
        }

        if (stored is null)
        {
            _logger.LogDebug("Attestation challenge was missing or already used");
            return new ChallengeValidation(ChallengeStatus.Missing);
        }

        StoredChallenge entry;

        try
        {
            entry = JsonSerializer.Deserialize<StoredChallenge>(stored)
                ?? throw new JsonException("The stored challenge was empty.");
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Attestation challenge payload was malformed");
            return new ChallengeValidation(ChallengeStatus.Malformed);
        }

        if (!AttestationEncoding.FixedTimeEquals(entry.Challenge, challenge))
        {
            _logger.LogWarning("Attestation challenge did not match its cache key");
            return new ChallengeValidation(ChallengeStatus.Malformed);
        }

        if (entry.ExpiresAt <= _time.GetUtcNow())
        {
            _logger.LogDebug("Attestation challenge had expired");
            return new ChallengeValidation(ChallengeStatus.Expired);
        }

        if (!AttestationEncoding.FixedTimeEquals(entry.DeviceId, deviceId))
        {
            _logger.LogWarning("Attestation challenge was presented by a different device");
            return new ChallengeValidation(ChallengeStatus.DeviceMismatch);
        }

        if (!string.Equals(entry.Action, expectedAction, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Attestation challenge action {ActualAction} did not match {ExpectedAction}",
                entry.Action,
                expectedAction);
            return new ChallengeValidation(ChallengeStatus.ActionMismatch);
        }

        if (expectedRequestHash is not null
            && !AttestationEncoding.FixedTimeEquals(entry.RequestHash, expectedRequestHash))
        {
            _logger.LogWarning("Attestation challenge was bound to a different request");
            return new ChallengeValidation(ChallengeStatus.RequestMismatch);
        }

        return new ChallengeValidation(ChallengeStatus.Valid, entry);
    }
}
