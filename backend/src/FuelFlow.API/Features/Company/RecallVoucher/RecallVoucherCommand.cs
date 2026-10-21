using FuelFlow.API.Features.Orders.SharedServices;
using FuelFlow.Features.Company.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Company.RecallVoucher;

public sealed record RecallVoucherCommand(Guid OwnerUserId, Guid VoucherId, Guid? LegalEntityId = null);

public sealed record RecallVoucherResult(string Status, string? ErrorMessage = null);

public sealed class RecallVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlowMetrics _metrics;

    public RecallVoucherCommandHandler(ApplicationDbContext context, FuelFlowMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<RecallVoucherResult> HandleAsync(RecallVoucherCommand command, CancellationToken cancellationToken = default)
    {
        var resolution = await _context.ResolveOwnedLegalEntityAsync(command.OwnerUserId, command.LegalEntityId, cancellationToken);

        if (resolution.ExplicitNotOwned)
        {
            return new RecallVoucherResult("CompanyNotOwned", "Company not found.");
        }

        if (resolution.LegalEntityId is not { } legalEntityId)
        {
            return new RecallVoucherResult("OwnerCompanyNotFound", "Owner does not have a legal entity profile.");
        }

        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(x => x.Id == command.VoucherId, cancellationToken);

        if (voucher is null)
        {
            return new RecallVoucherResult("NotFound", "Voucher not found.");
        }

        if (voucher.LegalEntityId != legalEntityId || voucher.AssignedToUserId != command.OwnerUserId)
        {
            return new RecallVoucherResult("Forbidden", "Voucher does not belong to this company owner.");
        }

        if (voucher.Status != VoucherStatus.Assigned || voucher.WorkerUserId is null)
        {
            return new RecallVoucherResult("InvalidState", "Only assigned gifted vouchers can be recalled.");
        }

        // Captured before the restore below rewrites it: the movement's order_id is the order this
        // recall is a *consequence of* — the gift that put the paper in the worker's hands — matching
        // how the renewal writers point `returned` at the renewal order rather than at the purchase the
        // returned voucher originally came from.
        var issuanceOrderId = voucher.OrderId;

        // The fuel goes back to the company, so the voucher goes back under the purchase it arrived
        // with. Leaving it on the issuance order would keep the handover showing a voucher the worker
        // no longer has, and the voucher would keep belonging to an order that no longer describes it.
        var restored = await IssuanceOrderLink.RestoreToPurchasesAsync(
            _context,
            [voucher.Id],
            cancellationToken);

        voucher.WorkerUserId = null;
        if (restored.TryGetValue(voucher.Id, out var orderId))
            voucher.OrderId = orderId;
        voucher.UpdatedAtUtc = SystemClock.UtcNow;

        _context.Update(voucher);

        // D-13. `recall` is the movement a worker's hand-off takes back, and this is its path: the
        // paper goes from a worker's hands back to the company that issued it. A frozen voucher
        // reaches the same place through the other door (ReturnVoucherToPoolCommand, planning
        // #558), which writes the same kind and adds the status change the thaw cannot.
        //
        // No StatusChanged event on purpose — the guard above required `Assigned` and nothing below
        // changes it. A recall takes the voucher back but does not make it a different kind of thing,
        // so a status row here would claim a transition that did not happen. `FireWorkerCommand` is the
        // one that does change status, and it writes its own event.
        _context.VoucherMovements.Add(new VoucherMovement
        {
            Id = Guid.CreateVersion7(),
            Kind = VoucherMovementKinds.Recall,
            VoucherId = voucher.Id,
            FromLocation = VoucherLocations.Worker,
            ToLocation = VoucherLocations.Company,
            OrderId = issuanceOrderId,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = command.OwnerUserId,
            ActorPartyId = legalEntityId,
            OccurredAtUtc = voucher.UpdatedAtUtc
        });

        await _context.SaveChangesAsync(cancellationToken);

        _metrics.VoucherRecalled();

        return new RecallVoucherResult("Success");
    }
}
