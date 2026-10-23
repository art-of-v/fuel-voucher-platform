using System.Linq.Expressions;
using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Vouchers;

/// <summary>
/// The rule that a paid voucher is never deleted, in one place so the single and bulk delete paths
/// cannot drift apart.
/// <para>
/// #217 turned the hard delete into a soft one, which was the right change: the row survives, so a
/// voucher's history - an operator-entered cost, an exchange it was the surrendered half of, the
/// stock a valuation was computed from - cannot be destroyed by deleting it. But the hard delete had
/// been protected by something nobody intended: RESTRICT foreign keys made it throw for any voucher
/// that had been fulfilled, exchanged or renewed. Soft-deleting removed that accidental refusal, and
/// nothing replaced it, so both paths went on to archive paid vouchers silently.
/// </para>
/// <para>
/// A voucher is treated as paid or held when it has an order, an owner, or any status past the
/// import checks. What remains archivable is stock that has never left stock.
/// </para>
/// </summary>
internal static class VoucherDeletionGuard
{
    /// <summary>
    /// The one definition of stock that has never been paid for. Written as an expression so
    /// <c>delete_all</c> can filter it in SQL, and compiled once for the in-memory checks, so the
    /// two can never disagree about what counts as stock.
    /// <para>
    /// A replace releases ownership but keeps <c>order_id</c> on purpose, so <c>OrderId</c> is a
    /// stronger signal than the status: a voucher can read Available and still be history.
    /// </para>
    /// </summary>
    internal static Expression<Func<FuelVoucher, bool>> IsStock { get; } = voucher =>
        voucher.OrderId == null
        && voucher.AssignedToUserId == null
        && (voucher.Status == VoucherStatus.Imported
            || voucher.Status == VoucherStatus.DecodeIntegrityWarning
            || voucher.Status == VoucherStatus.DecodeIntegrityFailed
            || voucher.Status == VoucherStatus.Available);

    private static readonly Func<FuelVoucher, bool> IsStockCompiled = IsStock.Compile();

    /// <summary>
    /// True when this voucher has left stock and must not be archived - the negation of
    /// <see cref="IsStock"/>, so there is exactly one list of the archivable statuses.
    /// </summary>
    internal static bool IsPaidOrHeld(FuelVoucher voucher) => !IsStockCompiled(voucher);

    /// <summary>
    /// Why the voucher is refused, phrased for the admin who is about to be told no. Names the
    /// specific signal rather than returning a bare "cannot delete", because an admin deleting a
    /// mixed selection needs to know which of the vouchers in front of them is the problem.
    /// </summary>
    internal static string Reason(FuelVoucher voucher)
    {
        var what = voucher.Status switch
        {
            VoucherStatus.Assigned => $"status is {VoucherStatus.Assigned} - it is in a customer's hands",
            VoucherStatus.PendingReconciliation => "it is awaiting supplier reconciliation",
            VoucherStatus.Used => "it has already been used",
            VoucherStatus.Expired => "it has already expired",
            VoucherStatus.Blocked => "it is blocked",
            VoucherStatus.Deactivated => "it is deactivated",
            _ => "it is part of an order"
        };

        return $"Voucher {voucher.VoucherNumber} cannot be deleted: {what}. " +
               "A paid voucher is never deleted - it is the record of what the customer bought. " +
               "Use unarchive if this was archived by mistake.";
    }
}