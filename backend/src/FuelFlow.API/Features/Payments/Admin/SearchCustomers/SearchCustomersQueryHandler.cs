using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Payments.Admin.SearchCustomers;

/// <summary>
/// Searches customers by name, phone or settlement code, bounded.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not the existing <c>GET /api/admin/users</c>. That one materialises the whole
/// table — its handler ends in <c>ToList()</c> with no limit — which is fine for a screen that
/// shows everyone once and unusable as a search-as-you-type backing query. Fixing that endpoint
/// properly is a separate change with its own callers; this is a bounded query for one picker.
/// </para>
/// <para>
/// The same fields are searched as the users screen, plus the settlement code, because the
/// operator arriving at this screen has one of the two in front of them.
/// </para>
/// </remarks>
public sealed class SearchCustomersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public SearchCustomersQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CustomerSearchHit>> HandleAsync(
        SearchCustomersQuery query,
        CancellationToken cancellationToken = default)
    {
        var term = (query.Search ?? string.Empty).Trim().ToLowerInvariant();
        if (term.Length < 2)
        {
            // Two characters minimum. A one-character search matches most of the table, and the
            // point of a bounded query is defeated the moment the bound is the result set.
            return Array.Empty<CustomerSearchHit>();
        }

        var capped = Math.Clamp(query.Limit, 1, 50);

        var hits = await _context.Users
            .AsNoTracking()
            .Where(u =>
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(term)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(term)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(term)) ||
                (u.SettlementCode != null && u.SettlementCode.ToLower().Contains(term)))
            // Settlement code first, then a name, then the phone: an exact code is the most certain
            // match a person can type, and an exact match should not sit below a lucky substring.
            .OrderByDescending(u => u.SettlementCode == term.ToUpperInvariant())
            .ThenBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Take(capped)
            .Select(u => new CustomerSearchHit(
                u.Id,
                u.PhoneNumber,
                u.SettlementCode,
                string.IsNullOrEmpty(u.FirstName) && string.IsNullOrEmpty(u.LastName)
                    ? null
                    : (u.FirstName + " " + u.LastName).Trim()))
            .ToListAsync(cancellationToken);

        return hits;
    }
}
