using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.GetMembers;

public sealed record GetMembersQuery(Guid OwnerUserId);

public sealed record CompanyMemberDto(
    Guid Id,
    Guid WorkerUserId,
    string WorkerPhoneNumber,
    string? WorkerFirstName,
    string? WorkerLastName,
    DateTime JoinedAtUtc,
    int GiftedVoucherCount);

public sealed class GetMembersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetMembersQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CompanyMemberDto>> HandleAsync(GetMembersQuery query, CancellationToken cancellationToken = default)
    {
        var legalEntityId = await _context.LegalEntities
            .AsNoTracking()
            .Where(x => x.UserId == query.OwnerUserId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (legalEntityId is null)
        {
            return Array.Empty<CompanyMemberDto>();
        }

        var giftedCounts = await _context.FuelVouchers
            .AsNoTracking()
            .Where(x =>
                x.LegalEntityId == legalEntityId.Value &&
                x.WorkerUserId != null &&
                x.Status == VoucherStatus.Assigned)
            .GroupBy(x => x.WorkerUserId!.Value)
            .Select(x => new { WorkerUserId = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var giftedCountByWorkerId = giftedCounts.ToDictionary(x => x.WorkerUserId, x => x.Count);

        var members = await _context.CompanyMembers
            .AsNoTracking()
            .Where(x => x.LegalEntityId == legalEntityId.Value)
            .Join(
                _context.Users.AsNoTracking(),
                member => member.WorkerUserId,
                user => user.Id,
                (member, user) => new { member, user })
            .OrderBy(x => x.user.LastName)
            .ThenBy(x => x.user.FirstName)
            .Select(x => new CompanyMemberDto(
                x.member.Id,
                x.member.WorkerUserId,
                x.user.PhoneNumber,
                x.user.FirstName,
                x.user.LastName,
                x.member.JoinedAtUtc,
                0))
            .ToListAsync(cancellationToken);

        return members
            .Select(x => x with { GiftedVoucherCount = giftedCountByWorkerId.GetValueOrDefault(x.WorkerUserId, 0) })
            .ToList();
    }
}
