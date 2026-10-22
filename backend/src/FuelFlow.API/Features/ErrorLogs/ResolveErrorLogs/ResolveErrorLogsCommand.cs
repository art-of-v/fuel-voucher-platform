namespace FuelFlow.Features.ErrorLogs.ResolveErrorLogs;

/// <summary>
/// The request to close (or reopen) a set of error records.
/// </summary>
/// <param name="Ids">The records to act on. A repeated id counts once.</param>
/// <param name="Resolved">True closes the records, false reopens the ones that are closed.</param>
/// <param name="ActingUserId">Who is acting. The journal stamps the decision with them, so a
/// record without an author would say who handled an incident by nobody.</param>
/// <param name="ActingUserName">The name that goes with the id, for the same stamp.</param>
public sealed record ResolveErrorLogsCommand(
    IReadOnlyList<Guid> Ids,
    bool Resolved,
    Guid? ActingUserId,
    string? ActingUserName);

/// <summary>
/// The answer to a resolve call: what happened, and - when it did not happen - why not.
/// </summary>
/// <remarks>
/// The refusals are separate answers rather than one "failed": an empty request, a caller the
/// platform cannot name, and a write that did happen are three different situations, and the
/// first two are the ones a reader of the audit log needs to tell apart.
/// </remarks>
public sealed record ResolveErrorLogsResult(string Status, int Changed, string? Error)
{
    public static ResolveErrorLogsResult NoIds() => new("NoIds", 0, "No error ids supplied");

    public static ResolveErrorLogsResult Unauthenticated() => new("Unauthenticated", 0, null);

    public static ResolveErrorLogsResult Done(int changed) => new("Done", changed, null);
}
