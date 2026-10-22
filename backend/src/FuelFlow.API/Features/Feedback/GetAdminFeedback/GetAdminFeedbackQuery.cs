namespace FuelFlow.Features.Feedback.GetAdminFeedback;

/// <summary>
/// The backlog's list query: which messages, and which page of them.
/// </summary>
/// <param name="Offset">Rows to skip. Clamped by <see cref="PageLimits.ClampOffset"/>.</param>
/// <param name="Limit">Rows per page. Clamped by <see cref="PageLimits.ClampPageSize"/>, so a
/// caller cannot ask for the whole backlog by asking for a large page.</param>
/// <remarks>
/// The verdict, category, severity and status arrive as the strings the admin's dropdowns send,
/// and an empty one means "the default" rather than "no filter" - which for the verdict is the
/// kept messages, because the whole point of the triage step is that the team reads fewer,
/// better rows.
/// </remarks>
public sealed record GetAdminFeedbackQuery(
    int Offset = 0,
    int Limit = 50,
    string? Verdict = null,
    string? Category = null,
    string? Severity = null,
    string? Status = null,
    string? Search = null,
    decimal? MaxConfidence = null,
    DateTime? From = null,
    DateTime? To = null);
