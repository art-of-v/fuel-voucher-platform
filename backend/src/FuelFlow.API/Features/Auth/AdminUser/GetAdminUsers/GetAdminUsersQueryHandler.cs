using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.AdminUser.GetAdminUsers;

public sealed class GetAdminUsersQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminUsersQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<AdminUserDto>> HandleAsync(GetAdminUsersQuery query, CancellationToken ct = default)
    {
        var items = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => !u.IsDeleted)
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(ct);

        if (query.Role != null)
            items = items.Where(u => u.Role?.Name == query.Role).ToList();

        if (query.IsActive.HasValue)
            items = items.Where(u => u.IsActive == query.IsActive.Value).ToList();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();

            // Settlement code is searchable on purpose: an operator receiving a settlement message
            // pastes the code rather than the customer's name, and without this the only way from the
            // code to the customer is to scroll the whole list.
            items = items.Where(u =>
                (u.PhoneNumber?.ToLowerInvariant().Contains(search) == true) ||
                (u.Email?.ToLowerInvariant().Contains(search) == true) ||
                (u.FirstName?.ToLowerInvariant().Contains(search) == true) ||
                (u.LastName?.ToLowerInvariant().Contains(search) == true) ||
                (u.SettlementCode?.ToLowerInvariant().Contains(search) == true)).ToList();
        }

        return items.Select(u => new AdminUserDto
        {
            Id = u.Id.ToString(),
            Phone = u.PhoneNumber,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Birthdate = u.Birthdate?.ToString("yyyy-MM-dd"),
            ProfileImageUrl = u.ProfileImageUrl,
            ReferralCode = u.ReferralCode,
            ReferredBy = u.ReferredBy,
            // The column is NOT NULL, so this only ever sees a value for a row that has been inserted. Empty
                // rather than null so the grid renders a visibly wrong cell instead of a blank one if
                // that ever stops being true.
                SettlementCode = u.SettlementCode ?? string.Empty,
            BonusBalance = u.BonusBalance,
            Role = u.Role?.Name,
            IsActive = u.IsActive,
            IsBanned = u.IsBanned,
            IsDeleted = u.IsDeleted,
            // Read-only visibility for staff. Without it the QA account is indistinguishable
            // from an ordinary customer in the users grid, so a tester reporting "the QA account
            // is broken" gives staff nothing to look at - the only QA surface in the panel was
            // the test-access switch, which says nothing about the account's own state. There is
            // deliberately no setter: the flag is granted by the seeding migration and by the
            // QA-gated registration path, never from the admin UI.
            IsQaAccount = u.IsQaAccount,
            CreatedAt = u.CreatedAtUtc.ToString("o")
        }).ToList();
    }
}

public sealed class AdminUserDto
{
    public string Id { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Birthdate { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? ReferralCode { get; set; }
    public string? ReferredBy { get; set; }

    /// <summary>
    /// Read-only, staff-only, no setter anywhere. The operator quotes this to attribute money to a
    /// customer, so the panel is the only place it may be surfaced - it must not be added to any
    /// customer-facing response, and no endpoint may accept it as an input.
    /// </summary>
    public string SettlementCode { get; set; } = null!;

    public decimal BonusBalance { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }
    public bool IsBanned { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsQaAccount { get; set; }
    public string CreatedAt { get; set; } = null!;
}
