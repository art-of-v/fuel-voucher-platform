using FuelFlow.SharedKernel.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace FuelFlow.SharedKernel.Services;

public sealed class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;

    public RedisCacheService(IDistributedCache cache) => _cache = cache;

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
}
