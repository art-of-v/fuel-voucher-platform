namespace FuelFlow.Features.ErrorLogs.ClearErrorLogs;

/// <summary>
/// The request to empty the journal.
/// </summary>
/// <param name="Before">Only the rows logged at or before this instant. Null empties the
/// whole journal.</param>
public sealed record ClearErrorLogsCommand(DateTime? Before = null);
