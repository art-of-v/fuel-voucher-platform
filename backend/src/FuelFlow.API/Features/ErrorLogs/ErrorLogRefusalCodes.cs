namespace FuelFlow.Features.ErrorLogs;

/// <summary>
/// The stable codes an error-log action is refused under, so the admin can say the reason in the
/// operator's language instead of printing the English sentence.
/// </summary>
internal static class ErrorLogRefusalCodes
{
    public const string NoIds = "errorlogs_no_ids";
}
