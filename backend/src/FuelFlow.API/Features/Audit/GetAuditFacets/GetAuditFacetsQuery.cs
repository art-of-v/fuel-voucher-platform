namespace FuelFlow.Features.Audit.GetAuditFacets;

/// <summary>The distinct values the audit journal holds, for the admin's filter dropdowns.</summary>
public sealed record GetAuditFacetsQuery;

/// <summary>
/// The values the journal actually contains: the event types, the aggregate types and the people
/// who have changed something. The dropdowns list these rather than every enum member the code
/// knows about, so an operator is never offered a filter that matches nothing.
/// </summary>
public sealed record AuditFacetsResponse(
    IReadOnlyList<string> EventTypes,
    IReadOnlyList<string> AggregateTypes,
    IReadOnlyList<string> Users);
