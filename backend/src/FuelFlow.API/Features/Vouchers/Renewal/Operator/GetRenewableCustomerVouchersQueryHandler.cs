using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Renewal.Operator;

/// <summary>
/// Finds customer-owned vouchers an operator may renew: assigned to a customer
/// (<c>AssignedToUserId != null</c>), not deleted, and in a renewable status (<c>Assigned</c> or
/// <c>Expired</c> — see <see cref="VoucherRenewalEligibility.IsRenewableStatus"/>). Unlike the mobile
/// flow there is no trigger-window gate: an operator acts deliberately and may renew any renewable
/// customer voucher. Read-only.
/// </summary>
public sealed record GetRenewableCustomerVouchersQuery(string? Query, string? Provider, string? Status);

public sealed class RenewableCustomerVoucherDto
{
    public Guid Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string FuelTypeId { get; set; } = string.Empty;
    public string? FuelName { get; set; }
    public decimal Liters { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Days until expiry (negative = already lapsed) as of today — drives the extend/replace hint and badge.</summary>
    public int DaysLeft { get; set; }

    public Guid OwnerUserId { get; set; }
    public string? OwnerName { get; set; }
}

public sealed class GetRenewableCustomerVouchersQueryHandler
{
    private const int MaxResults = 50;

    private readonly ApplicationDbContext _context;

    public GetRenewableCustomerVouchersQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<RenewableCustomerVoucherDto>> HandleAsync(
        GetRenewableCustomerVouchersQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(v => v.FuelType)
            .Include(v => v.AssignedToUser)
            .Where(v => !v.IsDeleted
                     && v.AssignedToUserId != null
                     && (v.Status == VoucherStatus.Assigned || v.Status == VoucherStatus.Expired));

        if (!string.IsNullOrWhiteSpace(query.Provider))
            q = q.Where(v => v.Provider == query.Provider);

        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<VoucherStatus>(query.Status, true, out var parsedStatus))
            q = q.Where(v => v.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var term = query.Query.Trim();
            q = q.Where(v =>
                EF.Functions.ILike(v.VoucherNumber, $"%{term}%")
                || (v.AssignedToUser != null && (
                       EF.Functions.ILike(v.AssignedToUser.PhoneNumber, $"%{term}%")
                    || (v.AssignedToUser.FirstName != null && EF.Functions.ILike(v.AssignedToUser.FirstName, $"%{term}%"))
                    || (v.AssignedToUser.LastName != null && EF.Functions.ILike(v.AssignedToUser.LastName, $"%{term}%")))));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var items = await q
            .OrderBy(v => v.ExpirationDate)
            .Take(MaxResults)
            .ToListAsync(cancellationToken);

        return items.Select(v => new RenewableCustomerVoucherDto
        {
            Id = v.Id,
            VoucherNumber = v.VoucherNumber,
            Provider = v.Provider,
            FuelTypeId = v.FuelTypeId,
            FuelName = v.FuelType?.Name,
            Liters = v.Liters,
            ExpirationDate = v.ExpirationDate,
            Status = v.Status.ToString(),
            DaysLeft = v.ExpirationDate.DayNumber - today.DayNumber,
            OwnerUserId = v.AssignedToUserId!.Value,
            OwnerName = ComposeName(v.AssignedToUser),
        }).ToList();
    }

    private static string? ComposeName(SharedKernel.Domain.User? user)
    {
        if (user is null) return null;
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? user.PhoneNumber : name;
    }
}
