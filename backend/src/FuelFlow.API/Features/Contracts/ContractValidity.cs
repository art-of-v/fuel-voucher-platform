using FuelFlow.Features.Contracts.SharedModels;

namespace FuelFlow.Features.Contracts;

/// <summary>
/// The one rule about a contract's validity: it can be signed only while it is still valid.
/// <para>
/// A contract has a validity term since 2026-10-20, on the business's instruction - a contract
/// without a term is not a contract, it is a record that something once happened. The term is
/// only worth having if it stops something, and what it stops is a signature: a signature that
/// outlives the agreement it is a signature of is a contract that still binds.
/// </para>
/// <para>
/// No database and no entity, so it stays trivially testable - the same shape as
/// <see cref="FuelFlow.Features.Vouchers.Renewal.VoucherRenewalEligibility"/>, which answers the
/// same kind of question for a voucher.
/// </para>
/// </summary>
public static class ContractValidity
{
    /// <summary>Whether the contract's term has run out, as of <paramref name="now"/>.</summary>
    public static bool IsExpired(DateTime expiresAtUtc, DateTime now) => expiresAtUtc <= now;

    /// <summary>Whether the contract may still be signed, as of <paramref name="now"/>.</summary>
    public static bool CanBeSigned(DateTime expiresAtUtc, DateTime now) => !IsExpired(expiresAtUtc, now);
}
