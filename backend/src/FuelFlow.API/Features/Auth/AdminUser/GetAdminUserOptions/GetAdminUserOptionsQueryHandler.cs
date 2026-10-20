using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.AdminUser.GetAdminUserOptions;

/// <summary>
/// The names of every customer, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Three screens need to name a customer who is not on the page the admin is looking at: the
/// report's customer filter, the reconciliation header, and the client column of the purchases
/// table. The customers table itself is paged - it shows fifty rows at a time - so the list it
/// reads cannot be the source of those names.
/// </para>
/// <para>
/// <b>Why this endpoint and not the full customer.</b> It carries four fields, not twenty. The
/// full customer carries the referral code, the bonus balance, the role, the ban flag, the QA
/// flag, the image URL and the birthdate, and none of them is a name. A dropdown wants a name.
/// </para>
/// <para>
/// <b>What it does not solve.</b> A dropdown of every customer is a dropdown of every customer: the
/// list grows with the platform, and a platform with a hundred thousand customers has a dropdown
/// nobody can use. The bounded picker - <c>GET /api/admin/payments/customers</c>, which
/// searches by name, phone or settlement code - is the answer for a screen that lets an operator
/// CHOOSE a customer; this endpoint is for the screens that need to NAME one they already have.
/// </para>
/// </remarks>
public sealed class GetAdminUserOptionsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminUserOptionsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<AdminUserOptionDto>> HandleAsync(CancellationToken ct = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Select(u => new AdminUserOptionDto
            {
                Id = u.Id.ToString(),
                Phone = u.PhoneNumber,
                FirstName = u.FirstName,
                LastName = u.LastName
            })
            .ToListAsync(ct);
    }
}

public sealed class AdminUserOptionDto
{
    public string Id { get; set; } = null!;
    public string? Phone { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
