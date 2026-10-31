namespace FuelFlow.Features.Feedback;

/// <summary>
/// The stable codes a feedback action is refused under, so the admin can say the reason in the
/// operator's language instead of printing the English sentence.
/// </summary>
internal static class FeedbackRefusalCodes
{
    public const string NoIds = "feedback_no_ids";

    public const string UnknownStatus = "feedback_unknown_status";

    public const string DeleteRefused = "feedback_delete_refused";
}
