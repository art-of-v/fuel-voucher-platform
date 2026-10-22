using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.AdminUser.GetAdminUsers;

public sealed class GetAdminUsersQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminUsersQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<PagedResult<AdminUserDto>> HandleAsync(GetAdminUsersQuery query, CancellationToken ct = default)
    {
        // The filters are the DATABASE's job. This used to load every user into the process and
        // filter the list there, so a search for one customer read the whole table to answer it -
        // the endpoint already advertised role, isActive and q, and the handler ignored them
        // until the rows were already in memory.
        var users = _context.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted);

        if (query.Role != null)
            users = users.Where(u => u.Role != null && u.Role.Name == query.Role);

        if (query.IsActive.HasValue)
            users = users.Where(u => u.IsActive == query.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();

            // Settlement code is searchable on purpose: an operator receiving a settlement message
            // pastes the code rather than the customer's name, and without this the only way from the
            // code to the customer is to scroll the whole list.
            users = users.Where(u =>
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(search)) ||
                (u.Email != null && u.Email.ToLower().Contains(search)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(search)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(search)) ||
                (u.SettlementCode != null && u.SettlementCode.ToLower().Contains(search)));
        }

        // The count is taken from the same query the page is: the filters are already the
        // database's job, and a count that ignored them would answer a different question.
        var total = await users.CountAsync(ct);

        var limit = PageLimits.ClampPageSize(query.Limit);

        var items = await users
            // The tie-break: many accounts share a registration timestamp, and without a unique
            // second key the sort is unstable - a row can move between pages.
            .OrderByDescending(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Skip(PageLimits.SkipFor(PageLimits.ClampPage(query.Page), limit))
            .Take(limit)
            .Select(u => new AdminUserDto
            {
                Id = u.Id.ToString(),
                Phone = u.PhoneNumber,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Birthdate = u.Birthdate.HasValue ? u.Birthdate.Value.ToString("yyyy-MM-dd") : null,
                ProfileImageUrl = u.ProfileImageUrl,
                ReferralCode = u.ReferralCode,
                ReferredBy = u.ReferredBy,
                // The column is NOT NULL, so this only ever sees a value for a row that has been inserted. Empty
                    // rather than null so the grid renders a visibly wrong cell instead of a blank one if
                    // that ever stops being true.
                SettlementCode = u.SettlementCode ?? string.Empty,
                BonusBalance = u.BonusBalance,
                Role = u.Role != null ? u.Role.Name : null,
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
                // Whether the staff account has a second factor enrolled. The operator needs it to
                // know that the "revoke the second factor" button has anything to revoke - and
                // that a staff account without one is the one to look at.
                HasTwoFactor = _context.StaffTwoFactors
                    .Any(f => f.UserId == u.Id && f.SecretCiphertext != null),
                CreatedAt = u.CreatedAtUtc.ToString("o")
            })
            .ToListAsync(ct);

        return new PagedResult<AdminUserDto>
        {
            Items = items,
            TotalCount = total,
            Page = PageLimits.ClampPage(query.Page),
            PageSize = limit
        };
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
    /// <para>
    /// It is money data, so the roles that may read it are the money tier (the
    /// <c>SettlementCode</c> capability); a caller without that capability gets an empty value here
    /// rather than the code. The controller fills it in - the handler does not know the caller.
    /// </para>
    /// </summary>
    public string SettlementCode { get; set; } = null!;

    public decimal BonusBalance { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }
    public bool IsBanned { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsQaAccount { get; set; }

    /// <summary>
    /// Whether the account has a second factor enrolled. Only a staff account can have one, and
    /// only an enrolled (proven) secret counts - a half-finished enrolment is not a factor.
    /// </summary>
    public bool HasTwoFactor { get; set; }

    public string CreatedAt { get; set; } = null!;
}
