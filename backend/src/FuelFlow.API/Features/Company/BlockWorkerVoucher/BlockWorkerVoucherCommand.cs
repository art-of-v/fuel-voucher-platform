using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.BlockWorkerVoucher;

public sealed record BlockWorkerVoucherCommand(Guid OwnerUserId, Guid VoucherId, Guid? LegalEntityId = null);

public sealed record BlockWorkerVoucherResult(string Status, string? ErrorMessage = null);

/// <summary>
/// Owner-side freeze of a voucher currently held by a worker (#103 S3b): the owner
/// of the active company flips an <c>Assigned</c> worker voucher to <c>Blocked</c> so the
/// worker can no longer redeem it (<see cref="Vouchers.MarkVoucherAsUsed"/> only accepts
/// <c>Assigned</c>), while the voucher stays assigned to that worker so the owner can
/// <see cref="UnblockWorkerVoucher"/> it back. Distinct from the admin block (fire-worker /
/// staff tooling): scoped to the caller's own company, keeps the worker link, and never
/// returns the voucher to the pool — recall is the separate action for that.
/// </summary>
public sealed class BlockWorkerVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public BlockWorkerVoucherCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<BlockWorkerVoucherResult> HandleAsync(BlockWorkerVoucherCommand command, CancellationToken cancellationToken = default)
    {
        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new BlockWorkerVoucherResult("CompanyNotOwned", "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new BlockWorkerVoucherResult("OwnerCompanyNotFound", "Owner does not have a legal entity profile.");
        }

        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(x => x.Id == command.VoucherId, cancellationToken);

        if (voucher is null)
        {
            return new BlockWorkerVoucherResult("NotFound", "Voucher not found.");
        }

        if (voucher.LegalEntityId != legalEntityId || voucher.AssignedToUserId != command.OwnerUserId)
        {
            return new BlockWorkerVoucherResult("Forbidden", "Voucher does not belong to this company owner.");
        }

        if (voucher.Status != VoucherStatus.Assigned || voucher.WorkerUserId is null)
        {
            return new BlockWorkerVoucherResult("InvalidState", "Only vouchers currently held by a worker can be blocked.");
        }

        // Repeat the status predicate in the UPDATE so a block cannot race a redemption:
        // exactly one of a concurrent block / mark-as-used wins.
        var affected = await _context.FuelVouchers
            .Where(v => v.Id == command.VoucherId
                        && v.Status == VoucherStatus.Assigned
                        && v.WorkerUserId != null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(v => v.Status, VoucherStatus.Blocked)
                    .SetProperty(v => v.UpdatedAtUtc, DateTime.UtcNow),
                cancellationToken);

        if (affected == 0)
        {
            return new BlockWorkerVoucherResult("InvalidState", "Only vouchers currently held by a worker can be blocked.");
        }

        _metrics.VouchersBlocked(1);

        return new BlockWorkerVoucherResult("Success");
    }
}
