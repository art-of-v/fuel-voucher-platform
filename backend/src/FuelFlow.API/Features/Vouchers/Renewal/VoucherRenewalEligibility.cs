using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Vouchers.Renewal;

/// <summary>Which fulfilment path a renewal takes once payment succeeds.</summary>
public enum VoucherRenewalBranch
{
    /// <summary>Old voucher is still valid: extend the SAME voucher in place (new expiry = old + term).</summary>
    Extend,

    /// <summary>Old voucher has run out of room: hand the customer a fresh one from stock, old ⇒ back to stock.</summary>
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
    /// Replace branch: the date we promise the customer — <b>what they paid for on top of what they
    /// already hold</b>. This is deliberately NOT <c>today + term</c>.
    /// </summary>
    /// <remarks>
    /// A customer paying for a 2-month term while their voucher is already good until 27.03 must be good
    /// until <b>27.05</b>, not until "today + 2 months": their leftover days are worth nothing if we
    /// hand them a fresh voucher starting from now. When their term has already lapsed (they renew an
    /// expired voucher) there is no leftover to keep, so the promise falls back to <c>today + term</c>.
    /// <para>
    /// The same value is the eligibility floor for the replacement stock: because the promise must fit
    /// inside the voucher we hand over, a candidate qualifies only when
    /// <c>stock.provider_expiration &gt;= PromisedExpirationForReplace(...)</c>. Picking the shortest
    /// qualifying voucher then stops Fuel Flow's near-expiry stock from lapsing unused, which is the
    /// whole point of the rule. Since the filter already guarantees the promise fits, the delivered date
    /// never has to be clamped.
    /// </para>
    /// </remarks>
    public static DateOnly PromisedExpirationForReplace(
        DateOnly today,
        DateOnly sourceCustomerExpiration,
        VoucherRenewalTerm term)
        => term.ApplyTo(sourceCustomerExpiration > today ? sourceCustomerExpiration : today);
}
