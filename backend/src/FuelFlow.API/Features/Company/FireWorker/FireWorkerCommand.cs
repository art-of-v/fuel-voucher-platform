using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.FireWorker;

public sealed record FireWorkerCommand(Guid OwnerUserId, Guid MemberId);

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
        var legalEntityId = await _context.LegalEntities
            .AsNoTracking()
            .Where(x => x.UserId == command.OwnerUserId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (legalEntityId is null)
        {
            return new FireWorkerResult("OwnerCompanyNotFound", ErrorMessage: "Owner does not have a legal entity profile.");
        }

        var member = await _context.CompanyMembers
            .FirstOrDefaultAsync(x => x.Id == command.MemberId && x.LegalEntityId == legalEntityId.Value, cancellationToken);

        if (member is null)
        {
            return new FireWorkerResult("NotFound", ErrorMessage: "Company member not found.");
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        var assignedWorkerVouchers = await _context.FuelVouchers
            .Where(x =>
                x.LegalEntityId == legalEntityId.Value &&
                x.WorkerUserId == member.WorkerUserId &&
                x.Status == VoucherStatus.Assigned)
            .ToListAsync(cancellationToken);

        foreach (var voucher in assignedWorkerVouchers)
        {
            voucher.WorkerUserId = null;
            voucher.Status = VoucherStatus.Blocked;
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
