using FuelFlow.SharedKernel.Abstractions;
using System.Collections.Concurrent;

namespace FuelFlow.SharedKernel.Services
{
    public sealed class InMemoryCacheService : ICacheService, IDisposable
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly ILogger<InMemoryCacheService> _logger;
        private readonly Timer _evictionTimer;
        private const int MaxEntries = 10_000;
        private const int EvictionBatchSize = 500;

        public InMemoryCacheService(ILogger<InMemoryCacheService> logger)
        {
            _logger = logger;
            _evictionTimer = new Timer(EvictExpiredEntries, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            var expiresAt = DateTime.UtcNow.Add(expiry);
            _cache[key] = new CacheEntry(value, expiresAt);

            if (_cache.Count > MaxEntries)
            {
                TrimOverflow();
            }

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

        public void Dispose()
        {
            _evictionTimer.Dispose();
        }

        private void EvictExpiredEntries(object? state)
        {
            var now = DateTime.UtcNow;
            int removed = 0;

            foreach (var kvp in _cache)
            {
                if (kvp.Value.ExpiresAt <= now && _cache.TryRemove(kvp.Key, out _))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                _logger.LogDebug("Background eviction removed {Count} expired cache entries", removed);
            }
        }

        private void TrimOverflow()
        {
            int excess = _cache.Count - MaxEntries;
            if (excess <= 0) return;

            int removed = 0;
            var entriesToRemove = _cache
                .OrderBy(kvp => kvp.Value.ExpiresAt)
                .Take(Math.Min(excess + EvictionBatchSize, _cache.Count / 2))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in entriesToRemove)
            {
                if (_cache.TryRemove(key, out _))
                {
                    removed++;
                }
            }

            _logger.LogDebug("Cache overflow: removed {Removed} oldest entries (excess: {Excess})", removed, excess);
        }

        private sealed record CacheEntry(string Value, DateTime ExpiresAt);
    }

}
