using FuelFlow.Features.Vouchers.Redemptions;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.MarkVoucherAsUsed;

public sealed class MarkVoucherAsUsedCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly VoucherRedemptionService _redemptions;
    private readonly ILogger<MarkVoucherAsUsedCommandHandler> _logger;

    public MarkVoucherAsUsedCommandHandler(
        ApplicationDbContext context,
        VoucherRedemptionService redemptions,
        ILogger<MarkVoucherAsUsedCommandHandler> logger)
    {
        _context = context;
        _redemptions = redemptions;
        _logger = logger;
    }

    public async Task<MarkVoucherAsUsedResponse> HandleAsync(
        MarkVoucherAsUsedCommand command,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Id == command.VoucherId)
            .Select(v => new
            {
                v.Id,
                v.Status,
                v.WorkerUserId,
                v.AssignedToUserId,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (voucher == null)
        {
            return new MarkVoucherAsUsedResponse(false, "Voucher not found", "NotFound");
        }

        // Same rule, and the same wording, as the endpoints that hand out the QR: VoucherHolder is the one
        // answer to "may this user redeem it", so this write can never permit what a read refuses.
        if (!VoucherHolder.CanRedeem(voucher.WorkerUserId, voucher.AssignedToUserId, command.UserId))
        {
            return new MarkVoucherAsUsedResponse(
                false,
                voucher.WorkerUserId.HasValue
                    ? "Only the assigned worker can use this voucher"
                    : "Voucher is not assigned to this user",
                "Forbidden");
        }

        if (voucher.Status == VoucherStatus.PendingReconciliation)
        {
            return new MarkVoucherAsUsedResponse(
                true,
                "Voucher is already awaiting supplier confirmation");
        }

        if (voucher.Status == VoucherStatus.Used)
        {
            return new MarkVoucherAsUsedResponse(true, "Voucher already marked as used");
        }

        if (voucher.Status != VoucherStatus.Assigned)
        {
            return new MarkVoucherAsUsedResponse(
                false,
                $"Voucher cannot be marked as used (current status: {voucher.Status})",
                "InvalidState");
        }

        var occurredAtUtc = SystemClock.UtcNow;

        // The transition, the claim, the audit event and the provisional journal entries are one
        // unit of work. A report that moved the status but left no draft would be a redemption
        // nobody can confirm later; a draft without the claim has no one to confirm it.
        var result = await _context.ExecuteReplaySafeAsync<RedemptionReportResult>(async ct =>
            await _redemptions.ReportAsync(
                command.VoucherId,
                reportedByUserId: command.UserId,
                actorUserId: command.UserId,
                reportedLiters: null,
                redeemedAtUtc: occurredAtUtc,
                supplierReference: null,
                reason: "Self-reported by the voucher holder",
                ct),
            cancellationToken,
            verifySucceeded: async ct => await _context.VoucherEvents
                .AsNoTracking()
                .AnyAsync(
                    e => e.VoucherId == command.VoucherId
                        && e.EventType == FuelFlow.Features.Vouchers.Events.VoucherEventTypes.StatusChanged
                        && e.ToStatus == nameof(VoucherStatus.PendingReconciliation),
                    ct));

        return result.Outcome switch
        {
            RedemptionReportOutcome.Reported =>
                new MarkVoucherAsUsedResponse(true, "Voucher reported; waiting for supplier confirmation"),
            RedemptionReportOutcome.AlreadyPending =>
                new MarkVoucherAsUsedResponse(true, "Voucher is already awaiting supplier confirmation"),
            RedemptionReportOutcome.AlreadyUsed =>
                new MarkVoucherAsUsedResponse(true, "Voucher already marked as used"),
            RedemptionReportOutcome.NotFound =>
                new MarkVoucherAsUsedResponse(false, "Voucher not found", "NotFound"),
            _ => new MarkVoucherAsUsedResponse(
                false,
                $"Voucher cannot be marked as used (current status: {result.CurrentStatus})",
                "InvalidState"),
        };
    }
}
