namespace FuelFlow.SharedKernel.Observability;

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public interface IAlertNotifier
{
    /// <summary>
    /// Sends an operational alert. Implementations must never throw: alerting is a
    /// side-channel and must not fail the request or job that triggered it.
    /// </summary>
    /// <param name="respectMinimumSeverity">
    /// When false the configured MinimumSeverity is ignored. Used by explicitly
    /// toggled domain-event notifications, where the per-event switch is already the
    /// gate and a global severity floor would silently swallow the message.
    /// </param>
    Task SendAsync(
        AlertSeverity severity,
        string title,
        string message,
        IReadOnlyDictionary<string, string>? context = null,
        bool respectMinimumSeverity = true,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Used when alerting is disabled or unconfigured, so call sites can depend on
/// <see cref="IAlertNotifier"/> unconditionally without null checks.
/// </summary>
public sealed class NullAlertNotifier : IAlertNotifier
{
    public Task SendAsync(
        AlertSeverity severity,
        string title,
        string message,
        IReadOnlyDictionary<string, string>? context = null,
        bool respectMinimumSeverity = true,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
