using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.GiftVouchers;

public sealed record GiftVouchersCommand(Guid OwnerUserId, Guid WorkerUserId, IReadOnlyList<Guid> VoucherIds);

public sealed record GiftVouchersResult(string Status, int GiftedCount = 0, string? ErrorMessage = null);

public sealed class GiftVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public GiftVouchersCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<GiftVouchersResult> HandleAsync(GiftVouchersCommand command, CancellationToken cancellationToken = default)
    {
        if (command.VoucherIds.Count == 0)
        {
            return new GiftVouchersResult("EmptyVoucherList", ErrorMessage: "At least one voucher must be provided.");
        }

        var legalEntityId = await _context.LegalEntities
            .AsNoTracking()
            .Where(x => x.UserId == command.OwnerUserId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (legalEntityId is null)
        {
            return new GiftVouchersResult("OwnerCompanyNotFound", ErrorMessage: "Owner does not have a legal entity profile.");
        }

        var isMember = await _context.CompanyMembers
            .AsNoTracking()
            .AnyAsync(x => x.LegalEntityId == legalEntityId.Value && x.WorkerUserId == command.WorkerUserId, cancellationToken);

        if (!isMember)
        {
            return new GiftVouchersResult("WorkerNotMember", ErrorMessage: "Worker is not a member of this company.");
        }

        var voucherIdSet = command.VoucherIds.Distinct().ToList();

        var vouchers = await _context.FuelVouchers
            .Where(x => voucherIdSet.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (vouchers.Count != voucherIdSet.Count)
        {
            return new GiftVouchersResult("VoucherNotFound", ErrorMessage: "One or more vouchers were not found.");
        }

        var invalidVoucher = vouchers.FirstOrDefault(v =>
            v.LegalEntityId != legalEntityId.Value ||
            v.AssignedToUserId != command.OwnerUserId ||
            v.WorkerUserId != null ||
            v.Status != VoucherStatus.Assigned);

        if (invalidVoucher is not null)
        {
            return new GiftVouchersResult(
                "VoucherNotEligible",
                ErrorMessage: "All vouchers must be company-owned pool vouchers with Assigned status and no worker assignment.");
        }

        foreach (var voucher in vouchers)
        {
            voucher.WorkerUserId = command.WorkerUserId;
            voucher.UpdatedAtUtc = DateTime.UtcNow;

            _context.Update(voucher);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _metrics.VouchersGifted(vouchers.Count);

        return new GiftVouchersResult("Success", vouchers.Count);
    }
}
