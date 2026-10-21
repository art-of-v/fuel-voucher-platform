namespace FuelFlow.Features.Vouchers.SharedModels;

/// <summary>
/// Who may currently redeem a voucher's QR.
/// <para>
/// Issuing fuel to a worker sets <see cref="FuelVoucher.WorkerUserId"/> and deliberately LEAVES
/// <see cref="FuelVoucher.AssignedToUserId"/> on the owner, because the owner keeps the purchase
/// lineage and the company's undistributed pool (#150). So "assigned to" is not "held by": after
/// issuing, the two columns name different people and only the worker holds the fuel.
/// </para>
/// <para>
/// Every endpoint that hands out redemption material - the wallet list, the PNG endpoint - must
/// answer through this one rule. They previously disagreed: <c>GET /vouchers/{id}/qr</c> refused
/// the owner with 403 while <c>GET /vouchers/my</c> still returned the payload and a rendered PNG,
/// so the second route handed the issuing owner the same bearer instrument the first one denied.
/// The owner could spend fuel they had just given away.
/// </para>
/// </summary>
public static class VoucherHolder
{
    /// <summary>
    /// The user who may redeem the voucher, or null when nobody holds it - operator stock,
    /// which has neither a worker nor an assignee. A worker, once set, wins: the owner no
    /// longer holds what they issued. A company's own pool voucher has no worker, so its
    /// holder is the owner - which is who may spend it.
    /// </summary>
    public static Guid? Of(Guid? workerUserId, Guid? assignedToUserId)
        => workerUserId ?? assignedToUserId;

    /// <summary>
    /// Whether this user may be given the voucher's QR payload or QR image.
    /// <see cref="MarkVoucherAsUsed.MarkVoucherAsUsedCommandHandler"/> and
    /// <see cref="RestoreVoucher.RestoreVoucherCommandHandler"/> enforce the same rule with their
    /// own wording; <c>VoucherQrScopeTests</c> pins the two to the same answers.
    /// </summary>
    public static bool CanRedeem(Guid? workerUserId, Guid? assignedToUserId, Guid userId)
        => Of(workerUserId, assignedToUserId) == userId;
}