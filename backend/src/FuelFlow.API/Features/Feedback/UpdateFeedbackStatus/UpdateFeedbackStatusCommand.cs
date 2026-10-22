namespace FuelFlow.Features.Feedback.UpdateFeedbackStatus;

/// <summary>
/// The request to move messages between review states.
/// </summary>
/// <param name="Ids">The messages to move. A repeated id counts once.</param>
/// <param name="Status">The state to move them to, as the admin's dropdown spells it.</param>
/// <param name="ActingUserId">Who is acting. The decision is audited with them, so a message
/// handled by nobody would be a message nobody handled.</param>
/// <param name="ActingUserName">The name that goes with the id, for the same audit row.</param>
public sealed record UpdateFeedbackStatusCommand(
    IReadOnlyList<Guid> Ids,
    string Status,
    Guid? ActingUserId,
    string? ActingUserName);

/// <summary>
/// The answer to a status change: what happened, and - when it did not happen - why not.
/// </summary>
public sealed record UpdateFeedbackStatusResult(string Status, int Affected, string? Error)
{
    public static UpdateFeedbackStatusResult NoIds() => new("NoIds", 0, "No feedback ids supplied");

    public static UpdateFeedbackStatusResult UnknownStatus(string status) =>
        new("UnknownStatus", 0, $"Unknown status '{status}'.");

    public static UpdateFeedbackStatusResult Unauthenticated() => new("Unauthenticated", 0, null);

    public static UpdateFeedbackStatusResult Done(int affected) => new("Done", affected, null);
}
