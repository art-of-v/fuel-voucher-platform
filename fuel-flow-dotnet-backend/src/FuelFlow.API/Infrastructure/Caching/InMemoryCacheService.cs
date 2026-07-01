using FuelFlow.Infrastructure.Caching;
using System.Collections.Concurrent;

namespace FuelFlow.API.Infrastructure.Caching
{
    public sealed class InMemoryCacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly ILogger<InMemoryCacheService> _logger;

        public InMemoryCacheService(ILogger<InMemoryCacheService> logger)
        {
            _logger = logger;
        }

        public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            var expiresAt = DateTime.UtcNow.Add(expiry);
            _cache[key] = new CacheEntry(value, expiresAt);

            _logger.LogDebug("Cached key {Key} with expiry {ExpiresAt}", key, expiresAt);

            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > DateTime.UtcNow)
                {
                    return Task.FromResult<string?>(entry.Value);
                }

                _cache.TryRemove(key, out _);
                _logger.LogDebug("Removed expired cache key {Key}", key);
            }

            return Task.FromResult<string?>(null);
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > DateTime.UtcNow)
                {
                    return Task.FromResult(true);
                }

                _cache.TryRemove(key, out _);
            }

            return Task.FromResult(false);
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _cache.TryRemove(key, out _);
            _logger.LogDebug("Removed cache key {Key}", key);
            return Task.CompletedTask;
        }

        private sealed record CacheEntry(string Value, DateTime ExpiresAt);
    }

}
