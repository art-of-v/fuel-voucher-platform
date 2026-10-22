using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Feedback.GetAdminFeedbackFacets;

/// <summary>
/// The distinct values of the backlog, and the triage split above it.
/// </summary>
/// <remarks>
/// The categories and the statuses are the values the table actually holds, so the dropdowns list
/// what exists rather than every enum member the code knows about. The totals are counted over
/// the WHOLE backlog, not over a page of it: a number that changed with the page would be a
/// number nobody could read.
/// </remarks>
public sealed class GetAdminFeedbackFacetsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminFeedbackFacetsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<FeedbackFacetsResponse> HandleAsync(
        GetAdminFeedbackFacetsQuery query,
        CancellationToken ct = default)
    {
        var categories = await _context.FeedbackMessages
            .AsNoTracking()
            .Where(m => m.Category != null)
            .Select(m => m.Category!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var statuses = await _context.FeedbackMessages
            .AsNoTracking()
            .Select(m => m.Status)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var totals = await _context.FeedbackMessages
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Kept = g.Count(m => m.Verdict == FeedbackVerdict.Kept),
                Junk = g.Count(m => m.Verdict == FeedbackVerdict.Junk),
                Unclassified = g.Count(m => m.Verdict == null),
                Replied = g.Count(m => m.StaffRepliedAtUtc != null)
            })
            .FirstOrDefaultAsync(ct);

        return new FeedbackFacetsResponse(
            categories,
            statuses,
            totals?.Total ?? 0,
            totals?.Kept ?? 0,
            totals?.Junk ?? 0,
            totals?.Unclassified ?? 0,
            totals?.Replied ?? 0);
    }
}
