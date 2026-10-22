namespace FuelFlow.Features.ErrorLogs.GetErrorLogs;

/// <summary>
/// The journal's list query: which error rows, and which page of them.
/// </summary>
/// <param name="Offset">Rows to skip. Clamped by <see cref="PageLimits.ClampOffset"/>.</param>
/// <param name="Limit">Rows per page. Clamped by <see cref="PageLimits.ClampPageSize"/>, so a
/// caller cannot ask for the whole journal by asking for a large page.</param>
/// <remarks>
/// The resolution filter is the one that decides what the journal opens on, and its default is
/// the outstanding faults only - an acknowledged fault has been dealt with.
/// </remarks>
public sealed record GetErrorLogsQuery(
    int Offset = 0,
    int Limit = 100,
    string? Level = null,
    string? Source = null,
    string? TraceId = null,
    ResolutionFilter Resolved = ResolutionFilter.Unresolved,
    string? Search = null,
    DateTime? From = null,
    DateTime? To = null);

/// <summary>The page of journal rows and the number of rows behind it.</summary>
public sealed record ErrorLogsResponse(int Total, IReadOnlyList<ErrorLogDto> Items);
