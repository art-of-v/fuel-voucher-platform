using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Vouchers.Renewal;

/// <summary>Which fulfilment path a renewal takes once payment succeeds.</summary>
public enum VoucherRenewalBranch
{
    /// <summary>Old voucher is still valid: extend the SAME voucher in place (new expiry = old + term).</summary>
    Extend,

    /// <summary>Old voucher has lapsed: hand the customer a fresh one from stock (valid ≥ today + term), old ⇒ Expired.</summary>
    Replace
}

/// <summary>
/// Pure rules deciding whether a voucher may be renewed and, if so, which branch it takes, plus the
/// two date primitives that keep the branches honest. No database, no entity — callers pass the
/// voucher's status and expiry, so this stays trivially testable.
/// </summary>
public static class VoucherRenewalEligibility
{
    /// <summary>
    /// Only a customer's own, unspent voucher can be renewed: it must be <see cref="VoucherStatus.Assigned"/>
    /// (still theirs) or <see cref="VoucherStatus.Expired"/> (lapsed but theirs). Spent (<c>Used</c>) and
    /// dead (<c>Blocked</c>/<c>Deactivated</c>) vouchers, and unassigned stock, are never renewable.
    /// </summary>
    public static bool IsRenewableStatus(VoucherStatus status)
        => status is VoucherStatus.Assigned or VoucherStatus.Expired;

    /// <summary>
    /// Decides eligibility and branch for a voucher as of <paramref name="today"/>. Eligible when the
    /// status is renewable AND the voucher is within the trigger window — its expiry is no later than
    /// <c>today + thresholdDays</c> (already-expired vouchers are always inside this window). Branch:
    /// still-valid (expiry ≥ today) ⇒ <see cref="VoucherRenewalBranch.Extend"/>; lapsed ⇒
    /// <see cref="VoucherRenewalBranch.Replace"/>.
    /// </summary>
    public static bool TryResolveBranch(
        VoucherStatus status,
        DateOnly expirationDate,
        DateOnly today,
        int thresholdDays,
        out VoucherRenewalBranch branch)
    {
        branch = default;

        if (!IsRenewableStatus(status))
            return false;

        if (expirationDate > today.AddDays(thresholdDays))
            return false;

        branch = expirationDate >= today
            ? VoucherRenewalBranch.Extend
            : VoucherRenewalBranch.Replace;
        return true;
    }

    /// <summary>
    /// Extend branch: the new expiry is the OLD expiry plus the chosen term — the customer's leftover
    /// days are kept, not forfeited. Explicitly NOT <c>today + term</c>.
    /// </summary>
    public static DateOnly NewExpirationForExtend(DateOnly currentExpiration, VoucherRenewalTerm term)
        => term.ApplyTo(currentExpiration);

    /// <summary>
    /// Replace branch: a stock voucher qualifies only if it stays valid at least until today + the
    /// chosen term. This is the minimum expiry a replacement voucher may have.
    /// </summary>
    public static DateOnly MinStockExpirationForReplace(DateOnly today, VoucherRenewalTerm term)
        => term.ApplyTo(today);
}
