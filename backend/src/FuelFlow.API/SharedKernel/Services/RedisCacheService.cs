using FuelFlow.SharedKernel.Abstractions;
using StackExchange.Redis;

namespace FuelFlow.SharedKernel.Services;

/// <summary>
/// Redis storage for <see cref="ICacheService"/>, with every entry stored as a plain Redis
/// string under the caller's key.
/// </summary>
/// <remarks>
/// <para>
/// The string shape is not an implementation detail - it is the contract the atomic operations
/// depend on. <c>GETDEL</c> and <c>SET ... NX</c> are string commands, and a value written in any
/// other shape makes them fail with <c>WRONGTYPE</c> for exactly the keys that exist.
/// </para>
/// <para>
/// This class used to go through <c>IDistributedCache</c>
/// for the plain reads and writes, and through the multiplexer for the atomic two. That mix is
/// what broke App Attest in production: the distributed cache stores every entry as a <b>hash</b>
/// (<c>absexp</c>/<c>sldexp</c>/<c>data</c>), so the challenge written by
/// <c>AttestationChallengeService.IssueAsync</c> was a hash and the <c>GETDEL</c> that consumed it
/// answered <c>WRONGTYPE</c> - every Apple key registration returned 503
/// <c>ATTESTATION_UNAVAILABLE</c>, and no Apple key was ever registered. The two shapes also
/// disagreed in the other direction: <c>TrySetIfAbsentAsync</c> wrote a string where
/// <c>GetAsync</c> would have tried to read a hash. One shape for every operation is the fix;
/// the integration suite exercises it against a real Redis.
/// </para>
/// </remarks>
public sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;

    /// <summary>
    /// The multiplexer is required rather than optional. DI supplies the same singleton the health
    /// check pings, so a Redis outage surfaces on <c>/health</c> instead of hiding behind a second
    /// connection. There used to be a second constructor path for the distributed cache;
    /// it was the path with the hash layout, and nothing legitimately constructs this class without
    /// a multiplexer.
    /// </summary>
    public RedisCacheService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private IDatabase Database => _redis.GetDatabase();

    public async Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Database.StringSetAsync(key, value, expiry);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var value = await Database.StringGetAsync(key);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await Database.KeyExistsAsync(key);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Database.KeyDeleteAsync(key);
    }

    public async Task<string?> GetAndDeleteAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var value = await Database.StringGetDeleteAsync(key);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task<bool> TrySetIfAbsentAsync(
        string key,
        string value,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await Database.StringSetAsync(key, value, expiry, When.NotExists);
    }
}
