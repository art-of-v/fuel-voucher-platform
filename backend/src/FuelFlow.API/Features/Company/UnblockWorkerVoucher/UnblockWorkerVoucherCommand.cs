using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.UnblockWorkerVoucher;

public sealed record UnblockWorkerVoucherCommand(Guid OwnerUserId, Guid VoucherId, Guid? LegalEntityId = null);

public sealed record UnblockWorkerVoucherResult(string Status, string? ErrorMessage = null);

/// <summary>
/// Owner-side thaw of a voucher the owner previously froze with
/// <see cref="BlockWorkerVoucher"/> (#103 S3b): flips a <c>Blocked</c> voucher back to
/// <c>Assigned</c>, leaving it with the same worker so it becomes redeemable again. The
/// one toggle counterpart to block; recalling to the pool stays a separate action.
/// </summary>
public sealed class UnblockWorkerVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public UnblockWorkerVoucherCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<UnblockWorkerVoucherResult> HandleAsync(UnblockWorkerVoucherCommand command, CancellationToken cancellationToken = default)
    {
        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new UnblockWorkerVoucherResult("CompanyNotOwned", "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new UnblockWorkerVoucherResult("OwnerCompanyNotFound", "Owner does not have a legal entity profile.");
        }

        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(x => x.Id == command.VoucherId, cancellationToken);

        if (voucher is null)
        {
            return new UnblockWorkerVoucherResult("NotFound", "Voucher not found.");
        }

        if (voucher.LegalEntityId != legalEntityId || voucher.AssignedToUserId != command.OwnerUserId)
        {
            return new UnblockWorkerVoucherResult("Forbidden", "Voucher does not belong to this company owner.");
        }

        if (voucher.Status != VoucherStatus.Blocked || voucher.WorkerUserId is null)
        {
            return new UnblockWorkerVoucherResult("InvalidState", "Only a worker's blocked voucher can be unblocked.");
        }

        var affected = await _context.FuelVouchers
            .Where(v => v.Id == command.VoucherId
                        && v.Status == VoucherStatus.Blocked
                        && v.WorkerUserId != null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.Assigned)
                    .SetProperty(v => v.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken);

        if (affected == 0)
        {
            return new UnblockWorkerVoucherResult("InvalidState", "Only a worker's blocked voucher can be unblocked.");
        }

        _metrics.VoucherUnblocked();

        return new UnblockWorkerVoucherResult("Success");
    }
}
