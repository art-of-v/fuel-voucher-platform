namespace FuelFlow.Features.ErrorLogs.GetErrorLogFacets;

/// <summary>The distinct values the journal holds, for the admin's filter dropdowns.</summary>
public sealed record GetErrorLogFacetsQuery;

/// <summary>
/// The levels and the sources the journal actually contains. The dropdowns list these rather
/// than every value the code knows about, so an operator is never offered a filter that matches
/// nothing.
/// </summary>
public sealed record ErrorLogFacetsResponse(
    IReadOnlyList<string> Levels,
    IReadOnlyList<string> Sources);
