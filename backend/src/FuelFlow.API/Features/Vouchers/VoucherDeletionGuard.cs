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
    internal static string Reason(FuelVoucher voucher) =>
        $"Voucher {voucher.VoucherNumber} cannot be deleted: {GapProse[Gap(voucher)]}. " +
        "A paid voucher is never deleted - it is the record of what the customer bought. " +
        "Use unarchive if this was archived by mistake.";

    /// <summary>
    /// Which signal refused the voucher, as a stable token the admin renders in its own language.
    /// </summary>
    /// <remarks>
    /// A token rather than the sentence, for the same reason every other refusal travels as a code:
    /// the panel is localized into four languages and this sentence is English. The tokens are the
    /// values of <c>extra.gap</c> on a <c>vouchers_not_deletable</c> refusal, and one of them per
    /// signal is what lets the operator read "it is in a customer's hands" in Ukrainian.
    /// <para>
    /// The status answers first, because "it has already been used" is what the operator needs to
    /// hear about a used voucher even though it also carries an order. The three fields answer after
    /// it, in the order <see cref="IsStock"/> checks them: a voucher whose status reads Available can
    /// still be refused for having been handed to a worker or kept as part of an order.
    /// </para>
    /// </remarks>
    internal static string Gap(FuelVoucher voucher) => voucher.Status switch
    {
        VoucherStatus.Assigned => "assigned",
        VoucherStatus.PendingReconciliation => "pending_reconciliation",
        VoucherStatus.Used => "used",
        VoucherStatus.Expired => "expired",
        VoucherStatus.Blocked => "blocked",
        VoucherStatus.Deactivated => "deactivated",
        _ when voucher.WorkerUserId != null => "worker",
        _ when voucher.AssignedToUserId != null => "assigned",
        _ => "order"
    };

    private static readonly IReadOnlyDictionary<string, string> GapProse = new Dictionary<string, string>
    {
        ["assigned"] = "it has been handed to its holder",
        ["pending_reconciliation"] = "it is awaiting supplier reconciliation",
        ["used"] = "it has already been used",
        ["expired"] = "it has already expired",
        ["blocked"] = "it is blocked",
        ["deactivated"] = "it is deactivated",
        ["worker"] = "it is with a worker",
        ["order"] = "it is part of an order"
    };
}