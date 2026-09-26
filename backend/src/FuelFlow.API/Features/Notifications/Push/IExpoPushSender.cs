namespace FuelFlow.Features.Notifications.Push;

/// <summary>
/// Sends already-persisted notifications to devices via the Expo Push service. Best-effort by
/// contract: implementations never throw — a push problem must never break notification
/// processing. Returns one result per input message (order preserved) so the caller can react
/// to per-token outcomes (e.g. deactivate a token Expo reports as DeviceNotRegistered).
/// </summary>
public interface IExpoPushSender
{
    Task<IReadOnlyList<ExpoPushResult>> SendAsync(
        IReadOnlyList<ExpoPushMessage> messages,
        CancellationToken cancellationToken);
}

/// <summary>A single push addressed to one Expo push token.</summary>
public sealed record ExpoPushMessage(
    string Token,
    string Title,
    string Body,
    IReadOnlyDictionary<string, object>? Data = null);

public enum ExpoPushStatus
{
    Ok,
    Error,
}

/// <summary>
/// The outcome for one message. <see cref="ErrorCode"/> is Expo's machine-readable
/// <c>details.error</c> (e.g. "DeviceNotRegistered") when <see cref="Status"/> is Error.
/// </summary>
public sealed record ExpoPushResult(string Token, ExpoPushStatus Status, string? ErrorCode);
