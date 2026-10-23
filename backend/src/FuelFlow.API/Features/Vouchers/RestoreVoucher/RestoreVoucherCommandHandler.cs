using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Redemptions;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.RestoreVoucher;

public sealed class RestoreVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly VoucherRedemptionService _redemptions;

    public RestoreVoucherCommandHandler(
        ApplicationDbContext context,
        VoucherRedemptionService redemptions)
    {
        _context = context;
        _redemptions = redemptions;
    }

    /// <summary>
    /// Withdraws the holder's own pending claim: <c>PendingReconciliation</c> back to
    /// <c>Assigned</c>. The mirror image of
    /// <see cref="MarkVoucherAsUsed.MarkVoucherAsUsedCommandHandler"/> under
    /// <c>FuelFlow.Features.Vouchers</c>.
    /// <para>
    /// A <c>Used</c> voucher is deliberately not restorable here. Once the evidence has been
    /// accepted the sale is posted, and undoing it is an operator reversal in an open period, not a
    /// customer toggle. The customer can still withdraw a report while it waits.
    /// </para>
    /// <para>
    /// This is <b>not</b> an un-delete. The global <c>!IsDeleted</c> query filter stays in force here,
    /// so an archived voucher is invisible and returns <c>NotFound</c> on purpose: un-archiving is an
    /// operator action and lives behind the admin endpoint, not something a customer can undo by id.
    /// </para>
    /// </summary>
    public async Task<RestoreVoucherResponse> HandleAsync(
        RestoreVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Id == command.VoucherId)
            .Select(v => new { v.Status, v.WorkerUserId, v.AssignedToUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (voucher == null)
        {
            return new RestoreVoucherResponse(false, "Voucher not found", "NotFound");
        }

        // Same rule, and the same wording, as MarkVoucherAsUsed and the endpoints that hand out the QR.
        if (!VoucherHolder.CanRedeem(voucher.WorkerUserId, voucher.AssignedToUserId, command.UserId))
        {
            return new RestoreVoucherResponse(
                false,
                voucher.WorkerUserId.HasValue
                    ? "Only the assigned worker can restore this voucher"
                    : "Voucher is not assigned to this user",
                "Forbidden");
        }

        if (voucher.Status == VoucherStatus.Assigned)
        {
            return new RestoreVoucherResponse(true, "Voucher is already in Assigned status");
        }

        if (voucher.Status != VoucherStatus.PendingReconciliation)
        {
            return new RestoreVoucherResponse(
                false,
                $"Voucher cannot be restored (current status: {voucher.Status}). "
                + "A confirmed redemption is reversed by an operator.",
                "InvalidState");
        }

        var result = await _context.ExecuteReplaySafeAsync(async ct =>
            await _redemptions.WithdrawAsync(
                command.VoucherId,
                command.UserId,
                note: "Withdrawn by the voucher holder",
                ct),
            cancellationToken,
            verifySucceeded: async ct =>
            {
                var latest = await _context.VoucherRedemptions
                    .AsNoTracking()
                    .Where(r => r.VoucherId == command.VoucherId)
                    .OrderByDescending(r => r.ReportedAtUtc)
                    .ThenByDescending(r => r.Id)
                    .Select(r => (VoucherRedemptionStatus?)r.Status)
                    .FirstOrDefaultAsync(ct);

                return latest == VoucherRedemptionStatus.Withdrawn;
            });

        return result.Outcome switch
        {
            RedemptionDecisionOutcome.Withdrawn =>
                new RestoreVoucherResponse(true, "Voucher restored to Assigned status"),
            RedemptionDecisionOutcome.AlreadyWithdrawn =>
                new RestoreVoucherResponse(true, "Voucher is already in Assigned status"),
            RedemptionDecisionOutcome.NotFound =>
                new RestoreVoucherResponse(false, "Voucher not found", "NotFound"),
            _ => new RestoreVoucherResponse(
                false,
                $"Voucher cannot be restored (current status: {result.CurrentStatus})",
                "InvalidState"),
        };
    }
}
