namespace FuelFlow.Features.Vouchers.SharedModels;

/// <summary>
/// Which voucher states still hold value, and therefore which of them must block the archiving of the
/// fuel they belong to.
/// <para>
/// Stated once, here, because the answer decides whether a row of paid-for paper can be made to
/// disappear from the catalogue — and getting it wrong is not a UI nuisance, it is the failure #217
/// was opened for: a fuel removed while customers still hold its vouchers leaves those vouchers
/// pointing at nothing, and nobody can price them.
/// </para>
/// </summary>
public static class VoucherStatuses
{
    /// <summary>
    /// A voucher that is still live, in any of the senses that matter: unsold stock, a voucher handed
    /// to a customer and not yet spent, or one blocked pending something.
    /// <para>
    /// <c>Used</c> is spent and <c>Expired</c> is dead, so neither blocks — a loss is recorded for an
    /// expired voucher separately. <c>DecodeIntegrityFailed</c> and <c>Deactivated</c> are excluded too:
    /// a voucher whose printed matrix failed the decode-integrity check or was switched off is not a claim
    /// on the fuel, and treating it as one would make a fuel impossible to retire until somebody cleaned
    /// up junk rows. That check is import QA, not a supplier-authenticity verdict.
    /// </para>
    /// </summary>
    public static readonly VoucherStatus[] Blocking =
    [
        VoucherStatus.Imported,
        VoucherStatus.DecodeIntegrityWarning,
        VoucherStatus.Available,
        VoucherStatus.Assigned,
        VoucherStatus.PendingReconciliation,
        VoucherStatus.Blocked,
    ];

    /// <summary>Whether a voucher in this state is still live.</summary>
    public static bool IsLive(VoucherStatus status) => Array.IndexOf(Blocking, status) >= 0;
}