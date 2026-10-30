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

    /// <summary>
    /// Stores the value only when the key is absent or expired, returning whether this call stored
    /// it. The claim and the expiry are one operation.
    /// </summary>
    /// <remarks>
    /// This is the alert throttle's primitive. A read-then-write would let two callers claim the
    /// same slot, and an in-process dictionary loses every claim on restart - which is how a
    /// redeploy repeated every alert that was already open. Redis answers it with
    /// <c>SET key value EX ttl NX</c>.
    /// </remarks>
    Task<bool> TrySetIfAbsentAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default);
}
