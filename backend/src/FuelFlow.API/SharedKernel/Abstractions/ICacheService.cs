using System.Collections.Concurrent;

namespace FuelFlow.SharedKernel.Abstractions;

public interface ICacheService
{
    Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
