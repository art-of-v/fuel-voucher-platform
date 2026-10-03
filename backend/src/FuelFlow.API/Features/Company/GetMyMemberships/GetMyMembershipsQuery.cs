using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.GetMyMemberships;

public sealed record GetMyMembershipsQuery(Guid WorkerUserId);

/// <summary>
/// One company the caller is an accepted member of ("worker context", epic #103 S5).
/// <see cref="IsOwner"/> is true when the caller also owns the entity — the client
/// then shows a single row with owner rights rather than a duplicate worker row.
/// </summary>
public sealed record MyCompanyMembershipDto(
    Guid MemberId,
    Guid LegalEntityId,
    string Name,
    string Edrpou,
    Guid OwnerUserId,
    bool IsOwner,
    DateTime JoinedAtUtc);

/// <summary>
/// Lists the companies the caller works for. Membership rows exist only while the
/// membership lives (firing deletes the row), so no status filter is needed —
/// unlike invitations, where a declined/cancelled row survives.
/// </summary>
public sealed class GetMyMembershipsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetMyMembershipsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<MyCompanyMembershipDto>> HandleAsync(
        GetMyMembershipsQuery query,
        CancellationToken cancellationToken = default)
    {
        return await _context.CompanyMembers
            .AsNoTracking()
            .Where(x => x.WorkerUserId == query.WorkerUserId)
            .Join(
                _context.LegalEntities.AsNoTracking(),
                member => member.LegalEntityId,
                legalEntity => legalEntity.Id,
                (member, legalEntity) => new { member, legalEntity })
            // Ordered on the joined entity, not on the projected DTO: Npgsql cannot
            // translate `OrderBy` over a record constructor parameter.
            .OrderBy(x => x.legalEntity.Name)
            .Select(x => new MyCompanyMembershipDto(
                x.member.Id,
                x.member.LegalEntityId,
                x.legalEntity.Name,
                x.legalEntity.Edrpou,
                x.legalEntity.UserId,
                x.legalEntity.UserId == query.WorkerUserId,
                x.member.JoinedAtUtc))
            .ToListAsync(cancellationToken);
    }
}