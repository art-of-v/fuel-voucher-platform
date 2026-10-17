namespace FuelFlow.Features.Vouchers.SharedModels;

/// <summary>
/// Who holds a voucher - the four-row ownership matrix from <c>MULTI_COMPANY.md</c> §4.3, expressed
/// once.
///
/// <para>
/// A voucher's three ownership columns are ambiguous by construction. Issuing fuel to a worker sets
/// <c>WorkerUserId</c> and deliberately LEAVES <c>AssignedToUserId</c> on the owner, so that after an
/// issuance the columns name two different people and "assigned to" is no longer the same question as
/// "held by". Reading the answer from the right column is the kind of thing every handler gets right
/// until one of them does not.
/// </para>
///
/// <para>
/// The values are produced by a STORED generated column, so this cannot drift from the columns it is
/// derived from, and <c>ck_voucher_holder</c> makes the combination a legal state rather than a
/// plausible one. Precedence is fixed and matters: a worker, once set, outranks the owner.
/// </para>
/// </summary>
public enum VoucherHolderKind
{
    /// <summary>Not handed to anyone. Still in the operator's or the company's pool.</summary>
    Stock = 0,

    /// <summary>Assigned straight to a customer who is not a company member.</summary>
    Personal = 1,

    /// <summary>
    /// Bought by a company and assigned to a customer who owns no company - a customer's own wallet,
    /// paid for by their company. Distinct from <see cref="Worker"/> only in whose fuel it is.
    /// </summary>
    Company = 2,

    /// <summary>Issued to a company member by the company that owns it. The fuel belongs to the worker.</summary>
    Worker = 3
}
