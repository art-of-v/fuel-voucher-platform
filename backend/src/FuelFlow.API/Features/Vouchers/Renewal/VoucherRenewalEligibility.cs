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
    /// status is renewable AND the customer term is no later than <c>today + thresholdDays</c>
    /// (already-expired vouchers are always inside this window). Branch:
    /// <see cref="VoucherRenewalBranch.Extend"/> when the customer term is still live **and** the
    /// voucher's real term has room for at least one offered tier; otherwise
    /// <see cref="VoucherRenewalBranch.Replace"/>.
    /// </summary>
    /// <remarks>
    /// The provider term matters as much as the date. We cannot extend a supplier voucher, so a still-valid
    /// customer voucher whose real term cannot absorb even the shortest offered tier has nothing to extend
    /// into — the only honest path is to swap it for longer-dated stock.
    /// </remarks>
    public static bool TryResolveBranch(
        VoucherStatus status,
        DateOnly customerExpiration,
        DateOnly providerExpiration,
        DateOnly today,
        int thresholdDays,
        IReadOnlyList<VoucherRenewalTerm> offeredTerms,
        out VoucherRenewalBranch branch)
    {
        branch = default;

        if (!IsRenewableStatus(status))
            return false;

        if (customerExpiration > today.AddDays(thresholdDays))
            return false;

        var canExtendInPlace = customerExpiration >= today
            && offeredTerms.Any(term => CanExtend(customerExpiration, providerExpiration, term));

        branch = canExtendInPlace
            ? VoucherRenewalBranch.Extend
            : VoucherRenewalBranch.Replace;
        return true;
    }

    /// <summary>
    /// Whether the voucher's real term can absorb <paramref name="term"/> on top of what the customer
    /// already holds. This is the ceiling: we may promise a customer more time only while the supplier's
    /// voucher still has that much life in it.
    /// </summary>
    public static bool CanExtend(DateOnly customerExpiration, DateOnly providerExpiration, VoucherRenewalTerm term)
        => NewExpirationForExtend(customerExpiration, term) <= providerExpiration;

    /// <summary>
    /// Extend branch: the new expiry is the OLD expiry plus the chosen term — the customer's leftover
    /// days are kept, not forfeited. Explicitly NOT <c>today + term</c>. The caller is responsible for
    /// checking the result against the provider term via <see cref="CanExtend"/>.
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
