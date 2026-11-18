using FluentAssertions;
using FuelFlow.Features.Attestation;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The one property the attestation rollout cannot survive without: everything the cache writes is
/// readable by everything the cache reads, including the atomic commands.
/// </summary>
/// <remarks>
/// <para>
/// This is the regression test for the defect that was only ever visible from production.
/// The cache wrote through <c>IDistributedCache</c>, which stores each entry as a Redis <b>hash</b>,
/// and consumed challenges with <c>GETDEL</c>, which is a <b>string</b> command. <c>GETDEL</c> on a
/// missing key returns nil, so the bug hid behind any test that consumed a challenge that was never
/// issued - while every real Apple key registration failed with <c>WRONGTYPE</c>, the API answered
/// 503 <c>ATTESTATION_UNAVAILABLE</c>, and <c>attestation_keys</c> stayed empty. 24 of the 28
/// registration attempts in the seven days to 2026-11-18 were that 503.
/// </para>
/// <para>
/// So the round trip has to be exercised against a real Redis, and it has to go through the
/// challenge service rather than only the cache interface: a cache test that only used
/// <c>SetAsync</c> and <c>GetAsync</c> would pass on a two-shape implementation.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class RedisCacheServiceTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RedisCacheServiceTests(TestDatabaseFixture fixture) => _fixture = fixture;
    [Fact]
    public async Task AStoredValueIsReadBackAsTheStringThatWasWritten()
    {
        var cache = NewCache();
        var key = NewKey();

        await cache.SetAsync(key, "value", TimeSpan.FromMinutes(1));

        (await cache.GetAsync(key)).Should().Be("value");
        (await cache.ExistsAsync(key)).Should().BeTrue();
    }

    [Fact]
    public async Task GetAndDelete_ReturnsTheValueOnceAndThenNothing()
    {
        var cache = NewCache();
        var key = NewKey();

        await cache.SetAsync(key, "challenge", TimeSpan.FromMinutes(1));

        (await cache.GetAndDeleteAsync(key)).Should().Be("challenge");
        (await cache.GetAndDeleteAsync(key)).Should().BeNull("the first call consumed the key");
        (await cache.ExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task TrySetIfAbsent_ClaimsTheSlotOnce()
    {
        var cache = NewCache();
        var key = NewKey();

        (await cache.TrySetIfAbsentAsync(key, "1", TimeSpan.FromMinutes(1))).Should().BeTrue();
        (await cache.TrySetIfAbsentAsync(key, "2", TimeSpan.FromMinutes(1))).Should().BeFalse();
        (await cache.GetAsync(key)).Should().Be("1");
    }

    /// <summary>
    /// The production path itself: a challenge is issued and then consumed by the registration
    /// every 1.2.0 iOS device runs before it can attest. Before the fix, the second half of this
    /// test threw <c>RedisServerException: WRONGTYPE</c> and the service surfaced it as
    /// <c>ATTESTATION_UNAVAILABLE</c>.
    /// </summary>
    [Fact]
    public async Task AnIssuedAttestationChallengeIsConsumedOnceByItsOwnDevice()
    {
        var cache = NewCache();
        var challenges = new AttestationChallengeService(
            cache,
            new FixedOptionsMonitor<AttestationOptions>(new AttestationOptions()),
            TimeProvider.System,
            NullLogger<AttestationChallengeService>.Instance);

        var deviceId = $"device-{Guid.NewGuid():N}";

        var issued = await challenges.IssueAsync(
            deviceId,
            AttestationChallengeService.AppleAttestAction,
            requestHash: string.Empty,
            CancellationToken.None);

        var consumed = await challenges.ConsumeAsync(
            issued.Challenge,
            deviceId,
            AttestationChallengeService.AppleAttestAction,
            expectedRequestHash: null,
            CancellationToken.None);

        consumed.IsValid.Should().BeTrue();

        var replay = await challenges.ConsumeAsync(
            issued.Challenge,
            deviceId,
            AttestationChallengeService.AppleAttestAction,
            expectedRequestHash: null,
            CancellationToken.None);

        replay.Status.Should().Be(ChallengeStatus.Missing, "a challenge is one-time");
    }

    private RedisCacheService NewCache() =>
        new(ConnectionMultiplexer.Connect(_fixture.RedisConnectionString));

    private static string NewKey() => $"it:cache:{Guid.NewGuid():N}";

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
