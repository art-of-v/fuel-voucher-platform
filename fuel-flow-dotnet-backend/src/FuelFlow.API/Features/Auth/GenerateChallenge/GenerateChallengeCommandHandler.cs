using FuelFlow.API.Features.Auth.GenerateChallenge;
using FuelFlow.Infrastructure.Caching;
using FuelFlow.Options;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace FuelFlow.Features.Auth.GenerateChallenge;

public sealed class GenerateChallengeCommandHandler
{
    private readonly ICacheService _cacheService;
    private readonly DeviceAuthOptions _options;
    private readonly ILogger<GenerateChallengeCommandHandler> _logger;

    public GenerateChallengeCommandHandler(
        ICacheService cacheService,
        IOptions<DeviceAuthOptions> options,
        ILogger<GenerateChallengeCommandHandler> logger)
    {
        _cacheService = cacheService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GenerateChallengeResponse> HandleAsync(
        GenerateChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Generating challenge for device {DeviceId}",
            command.DeviceId);

        var challengeBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(challengeBytes);
        }

        var challenge = Convert.ToBase64String(challengeBytes);
        var expiresAt = DateTime.UtcNow.AddSeconds(_options.ChallengeExpirySeconds);

        var cacheKey = $"challenge:{command.DeviceId}";
        await _cacheService.SetAsync(
            cacheKey,
            challenge,
            TimeSpan.FromSeconds(_options.ChallengeExpirySeconds),
            cancellationToken);

        _logger.LogInformation(
            "Challenge generated for device {DeviceId}, expires at {ExpiresAt}",
            command.DeviceId,
            expiresAt);

        return new GenerateChallengeResponse
        {
            Challenge = challenge,
            ExpiresAt = expiresAt
        };
    }
}
