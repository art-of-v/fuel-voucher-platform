using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Redemptions.DecideRedemption;

public enum RedemptionDecisionAction
{
    Confirm,
    Reject,
    Reverse,
}

public sealed record DecideRedemptionCommand(
    Guid VoucherId,
    RedemptionDecisionAction Action,
    Guid UserId,
    string? Note);

public sealed record DecideRedemptionResponse(
    bool Success,
    string Message,
    string? ErrorCode = null);

public sealed class DecideRedemptionCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly VoucherRedemptionService _redemptions;

    public DecideRedemptionCommandHandler(
        ApplicationDbContext context,
        VoucherRedemptionService redemptions)
    {
        _context = context;
        _redemptions = redemptions;
    }

    public async Task<DecideRedemptionResponse> HandleAsync(
        DecideRedemptionCommand command,
        CancellationToken cancellationToken = default)
    {
        var expectedStatus = command.Action switch
        {
            RedemptionDecisionAction.Confirm => VoucherRedemptionStatus.Confirmed,
            RedemptionDecisionAction.Reject => VoucherRedemptionStatus.Rejected,
            RedemptionDecisionAction.Reverse => VoucherRedemptionStatus.Reversed,
            _ => (VoucherRedemptionStatus?)null,
        };

        if (expectedStatus is null)
        {
            return new DecideRedemptionResponse(false, "Unknown redemption decision", "InvalidState");
        }

        var result = await _context.ExecuteReplaySafeAsync<RedemptionDecisionResult>(async ct =>
            command.Action switch
            {
                RedemptionDecisionAction.Confirm =>
                    await _redemptions.ConfirmAsync(command.VoucherId, command.UserId, command.Note, ct),
                RedemptionDecisionAction.Reject =>
                    await _redemptions.RejectAsync(command.VoucherId, command.UserId, command.Note, ct),
                _ => await _redemptions.ReverseAsync(command.VoucherId, command.UserId, command.Note, ct),
            },
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

                return latest == expectedStatus.Value;
            });

        return result.Outcome switch
        {
            RedemptionDecisionOutcome.Confirmed =>
                new DecideRedemptionResponse(true, "Redemption confirmed"),
            RedemptionDecisionOutcome.AlreadyConfirmed =>
                new DecideRedemptionResponse(true, "Redemption was already confirmed"),
            RedemptionDecisionOutcome.Rejected =>
                new DecideRedemptionResponse(true, "Redemption rejected; the voucher is assignable again"),
            RedemptionDecisionOutcome.AlreadyRejected =>
                new DecideRedemptionResponse(true, "Redemption was already rejected"),
            RedemptionDecisionOutcome.Reversed =>
                new DecideRedemptionResponse(true, "Redemption reversed"),
            RedemptionDecisionOutcome.AlreadyReversed =>
                new DecideRedemptionResponse(true, "Redemption was already reversed"),
            RedemptionDecisionOutcome.NotFound =>
                new DecideRedemptionResponse(false, "Voucher not found", "NotFound"),
            _ => new DecideRedemptionResponse(
                false,
                $"Voucher cannot take this decision (current status: {result.CurrentStatus})",
                "InvalidState"),
        };
    }
}
