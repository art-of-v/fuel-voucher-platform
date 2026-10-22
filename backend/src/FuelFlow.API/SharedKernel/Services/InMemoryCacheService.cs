using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Time;
using System.Collections.Concurrent;

namespace FuelFlow.SharedKernel.Services
{
    public sealed class InMemoryCacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
        private readonly ILogger<InMemoryCacheService> _logger;
        private readonly TimeProvider _clock;
        private const int MaxEntries = 10_000;

        /// <param name="logger">The logger.</param>
        /// <param name="clock">
        /// The clock the entries expire against. It is the application's clock unless a caller
        /// passes one of its own - which is how a test can move time forward instead of sleeping
        /// for the TTL to elapse.
        /// </param>
        public InMemoryCacheService(ILogger<InMemoryCacheService> logger, TimeProvider? clock = null)
        {
            _logger = logger;
            _clock = clock ?? SystemClock.Provider;
        }

        public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            var expiresAt = _clock.GetUtcNow().UtcDateTime.Add(expiry);
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
                if (entry.ExpiresAt > _clock.GetUtcNow().UtcDateTime)
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
                if (entry.ExpiresAt > _clock.GetUtcNow().UtcDateTime)
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

        public Task<string?> GetAndDeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!_cache.TryRemove(key, out var entry))
                return Task.FromResult<string?>(null);

            if (entry.ExpiresAt > _clock.GetUtcNow().UtcDateTime)
                return Task.FromResult<string?>(entry.Value);

            _logger.LogDebug("Discarded expired cache key {Key}", key);
            return Task.FromResult<string?>(null);
        }

        private void TrimOverflow()
        {
            int excess = _cache.Count - MaxEntries;
            if (excess <= 0) return;

            var entriesToRemove = _cache
                .OrderBy(kvp => kvp.Value.ExpiresAt)
                .Take(excess)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in entriesToRemove)
            {
                _cache.TryRemove(key, out _);
            }

            _logger.LogWarning("Cache overflow: removed {Count} oldest entries (excess: {Excess})", entriesToRemove.Count, excess);
        }

        private sealed record CacheEntry(string Value, DateTime ExpiresAt);
    }

}
