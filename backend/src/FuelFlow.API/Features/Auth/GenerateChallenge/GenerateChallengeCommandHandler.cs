using FuelFlow.API.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace FuelFlow.Features.Auth.GenerateChallenge;

public sealed class GenerateChallengeCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly DeviceAuthOptions _options;
    private readonly ILogger<GenerateChallengeCommandHandler> _logger;

    public GenerateChallengeCommandHandler(
        ApplicationDbContext context,
        ICacheService cacheService,
        IOptions<DeviceAuthOptions> options,
        ILogger<GenerateChallengeCommandHandler> logger)
    {
        _context = context;
        _cacheService = cacheService;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Builds the cache key for one issued challenge.
    /// <para>
    /// The key used to be "challenge:{DeviceId}" - one slot per device, holding the challenge
    /// value. This endpoint is anonymous and takes the device_id from the request body, so any
    /// caller who knew a device_id could request a challenge for it and overwrite whatever
    /// challenge that device was in the middle of signing, making the victim's verify fail with
    /// "Invalid challenge". Repeating it kept a device permanently unable to re-authenticate.
    /// device_id is not a secret: it travels as the x-device-id header on every request.
    /// </para>
    /// <para>
    /// Keying by the challenge value instead means a new challenge cannot displace an older
    /// outstanding one - both stay valid until their TTL - so there is nothing to overwrite. The
    /// value is irrelevant; presence of the key is the proof the server issued this challenge,
    /// which also replaces the string comparison the verify handler used to do.
    /// </para>
    /// </summary>
    internal static string CacheKey(string deviceId, string challenge) =>
        $"challenge:{deviceId}:{challenge}";

    public async Task<GenerateChallengeResponse> HandleAsync(
        GenerateChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        var challengeBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(challengeBytes);
        }

        var challenge = Convert.ToBase64String(challengeBytes);
        var expiresAt = DateTime.UtcNow.AddSeconds(_options.ChallengeExpirySeconds);

        // Only persist a challenge for a device that exists and is active. Storing one for any
        // attacker-supplied string let an anonymous caller grow the Redis keyspace at 5 writes
        // per minute per IP with no account and no device.
        //
        // The response is deliberately identical either way - a well-formed random challenge
        // with the same shape and timing. Returning an error for an unknown device_id would
        // turn this endpoint into an oracle for "is this device enrolled", which is worse than
        // the storage it saves. An unstored challenge simply fails at verify with the same
        // "Challenge not found or expired" an expired one gets.
        var deviceExists = await _context.Devices
            .AsNoTracking()
            .AnyAsync(
                d => d.DeviceId == command.DeviceId && d.Status == DeviceStatus.Active,
                cancellationToken);

        if (deviceExists)
        {
            await _cacheService.SetAsync(
                CacheKey(command.DeviceId, challenge),
                "1",
                TimeSpan.FromSeconds(_options.ChallengeExpirySeconds),
                cancellationToken);
        }

        // Debug, not Information: this is an anonymous endpoint, so the device_id being logged
        // is attacker-supplied at Information volume on every call.
        _logger.LogDebug(
            "Challenge issued for device {DeviceId} (stored={Stored}), expires at {ExpiresAt}",
            command.DeviceId,
            deviceExists,
            expiresAt);

        return new GenerateChallengeResponse
        {
            Challenge = challenge,
            ExpiresAt = expiresAt
        };
    }
}
