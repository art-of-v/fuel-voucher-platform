using FuelFlow.API.Features.Orders.SharedServices;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.FireWorker;

public sealed record FireWorkerCommand(Guid OwnerUserId, Guid MemberId, Guid? LegalEntityId = null);

public sealed record FireWorkerResult(string Status, int BlockedVoucherCount = 0, string? ErrorMessage = null);

public sealed class FireWorkerCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public FireWorkerCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<FireWorkerResult> HandleAsync(FireWorkerCommand command, CancellationToken cancellationToken = default)
    {
        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new FireWorkerResult("CompanyNotOwned", ErrorMessage: "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new FireWorkerResult("OwnerCompanyNotFound", ErrorMessage: "Owner does not have a legal entity profile.");
        }

        var member = await _context.CompanyMembers
            .FirstOrDefaultAsync(x => x.Id == command.MemberId && x.LegalEntityId == legalEntityId, cancellationToken);

        if (member is null)
        {
            return new FireWorkerResult("NotFound", ErrorMessage: "Company member not found.");
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        var assignedWorkerVouchers = await _context.FuelVouchers
            .Where(x =>
                x.LegalEntityId == legalEntityId &&
                x.WorkerUserId == member.WorkerUserId &&
                x.Status == VoucherStatus.Assigned)
            .ToListAsync(cancellationToken);

        // Fuel that leaves with the worker is company fuel again, so each voucher returns to the purchase it
        // was bought under. Vouchers already Blocked are not in the set above, so they keep their
        // issuance order: a frozen voucher stays the worker's, which is why the worker sees why it is
        // unusable.
        var restored = await IssuanceOrderLink.RestoreToPurchasesAsync(
            _context,
            assignedWorkerVouchers.Select(v => v.Id).ToList(),
            cancellationToken);

        foreach (var voucher in assignedWorkerVouchers)
        {
            voucher.WorkerUserId = null;
            voucher.Status = VoucherStatus.Blocked;
            if (restored.TryGetValue(voucher.Id, out var orderId))
                voucher.OrderId = orderId;
            voucher.UpdatedAtUtc = DateTime.UtcNow;
            _context.Update(voucher);
        }

        _context.CompanyMembers.Remove(member);
        await _context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Recorded after commit so a rolled-back transaction cannot inflate the counter.
        if (assignedWorkerVouchers.Count > 0)
        {
            _metrics.VouchersBlocked(assignedWorkerVouchers.Count);
        }

        return new FireWorkerResult("Success", assignedWorkerVouchers.Count);
    }
}
