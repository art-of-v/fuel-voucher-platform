namespace FuelFlow.Features.Audit.GetAuditEvents;

/// <summary>
/// The journal's list query: which audit rows, and which page of them.
/// </summary>
/// <param name="Offset">Rows to skip. Clamped by <see cref="PageLimits.ClampOffset"/>.</param>
/// <param name="Limit">Rows per page. Clamped by <see cref="PageLimits.ClampPageSize"/>, so a
/// caller cannot ask for the whole journal by asking for a large page.</param>
/// <remarks>
/// Every filter is plain text and every one of them is optional: an empty filter means "no
/// filter", not "nothing matches".
/// </remarks>
public sealed record GetAuditEventsQuery(
    int Offset = 0,
    int Limit = 100,
    string? EventType = null,
    string? AggregateType = null,
    string? ChangedBy = null,
    string? Search = null,
    DateTime? From = null,
    DateTime? To = null);

/// <summary>
/// The page of audit rows and the number of rows behind it. The count is of the FILTERED
/// journal, so it is the number the admin's pager divides by - not the number of rows in the
/// table.
/// </summary>
public sealed record AuditEventsResponse(int Total, IReadOnlyList<AuditEventDto> Events);
