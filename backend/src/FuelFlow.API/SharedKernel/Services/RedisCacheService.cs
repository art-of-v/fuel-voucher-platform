using FuelFlow.SharedKernel.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace FuelFlow.SharedKernel.Services;

public sealed class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer? _redis;

    /// <summary>
    /// The optional multiplexer is only needed for the atomic <see cref="GetAndDeleteAsync"/>
    /// operation. <see cref="IDistributedCache"/> has no GETDEL, so the cache and the one-time
    /// challenge store would otherwise need two implementations and two configuration paths.
    /// DI supplies the same singleton the health check pings; a test that constructs this class
    /// with only an <see cref="IDistributedCache"/> still works, with the non-atomic fallback.
    /// </summary>
    public RedisCacheService(IDistributedCache cache, IConnectionMultiplexer? redis = null)
    {
        _cache = cache;
        _redis = redis;
    }

    public async Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken ct = default)
    {
        await _cache.SetStringAsync(key, value, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry
        }, ct);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        await _cache.GetStringAsync(key, ct);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        await _cache.GetStringAsync(key, ct) is not null;

    public async Task RemoveAsync(string key, CancellationToken ct = default) =>
        await _cache.RemoveAsync(key, ct);

    public async Task<string?> GetAndDeleteAsync(string key, CancellationToken ct = default)
    {
        if (_redis is not null)
        {
            // GETDEL is atomic in Redis and is the whole reason this method exists.
            return await _redis.GetDatabase().StringGetDeleteAsync(key);
        }

        // No multiplexer: the caller gets the old non-atomic behaviour rather than a NullReference.
        // The production registration always supplies the multiplexer; this path is for tests and
        // for any future composition that still passes only the distributed cache.
        var value = await _cache.GetStringAsync(key, ct);
        await _cache.RemoveAsync(key, ct);
        return value;
    }

    public async Task<bool> TrySetIfAbsentAsync(
        string key,
        string value,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        if (_redis is not null)
        {
            // SET ... EX ttl NX is one command, so two callers cannot both claim the slot.
            return await _redis.GetDatabase().StringSetAsync(key, value, expiry, When.NotExists);
        }

        // Without the multiplexer the claim is best-effort - the same trade GetAndDeleteAsync makes
        // above rather than throwing a NullReference from a throttle.
        if (await _cache.GetStringAsync(key, ct) is not null)
            return false;

        await _cache.SetStringAsync(key, value, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry
        }, ct);

        return true;
    }
}
