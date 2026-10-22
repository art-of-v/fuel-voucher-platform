using FuelFlow.Features.Feedback.SharedModels;

namespace FuelFlow.Features.Feedback.DeleteAdminFeedback;

/// <summary>
/// The request to delete messages of the backlog.
/// </summary>
/// <param name="Id">One message to delete.</param>
/// <param name="Verdict">Every message with this verdict to delete.</param>
/// <remarks>
/// At least one of the two has to be named: an unscoped delete would empty the table on a
/// mistyped query string.
/// </remarks>
public sealed record DeleteAdminFeedbackCommand(Guid? Id, FeedbackVerdict? Verdict);

/// <summary>
/// The answer to a delete: what happened, and - when it did not happen - why not.
/// </summary>
public sealed record DeleteAdminFeedbackResult(string Status, int Affected, string? Error)
{
    public static DeleteAdminFeedbackResult Refused() => new("Refused", 0, "Supply either id or verdict.");

    public static DeleteAdminFeedbackResult Done(int affected) => new("Done", affected, null);
}
