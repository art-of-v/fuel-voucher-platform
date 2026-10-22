using System.Collections.Concurrent;

namespace FuelFlow.SharedKernel.Abstractions;

public interface ICacheService
{
    Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a key and removes it in one operation, returning the value that was stored or null when
    /// it was absent or already expired.
    /// </summary>
    /// <remarks>
    /// This exists for one-time challenges. <see cref="GetAsync"/> followed by
    /// <see cref="RemoveAsync"/> is two round trips, and two concurrent requests can both read the
    /// same challenge before either removes it. On a replay-protection path that is exactly the
    /// window an attacker wants. The in-memory store implements this with an atomic dictionary
    /// removal; Redis with <c>GETDEL</c>.
    /// </remarks>
    Task<string?> GetAndDeleteAsync(string key, CancellationToken cancellationToken = default);
}
