namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The capability policies of the staff surface — one name per capability.
/// </summary>
/// <remarks>
/// <para>
/// Until 2026-10-21 every staff endpoint carried the same attribute,
/// <c>[Authorize(Policy = "Staff")]</c>, so an endpoint said only that it was for staff and never
/// what it allowed. The finding that changed this (planning #521, S-1) is a disagreement between
/// two true statements: the codebase's own hierarchy says a Manager moves money and never changes
/// roles, and one policy cannot say which of those an endpoint does. A name that names the
/// capability settles it where a reader of the controller is already looking — on the attribute.
/// </para>
/// <para>
/// The names are constants rather than string literals at the call sites. A policy name that does
/// not exist is not a compile error: it fails at the first request with a 500, in production, on
/// the endpoint nobody tested. A typo in twenty controllers should be a build failure instead.
/// </para>
/// </remarks>
internal static class AuthPolicies
{
    /// <summary>
    /// Read the staff surface: the dashboards, the lists, the reports. It grants no write.
    /// </summary>
    public const string Staff = "Staff";

    /// <summary>
    /// Write the product catalogue: the brands, their fuels, their stations, the fuel types, the
    /// prices, the suppliers, and the contract text a provider's drivers sign.
    /// </summary>
    public const string CatalogueWrite = "CatalogueWrite";

    /// <summary>
    /// Write the voucher stock: the import and its batches, the operator's exchange and renewal,
    /// and the state of a voucher.
    /// </summary>
    public const string VoucherWrite = "VoucherWrite";

    /// <summary>
    /// Move money: a refund, a payment placed for a customer, an order's status.
    /// </summary>
    public const string MoneyMovement = "MoneyMovement";

    /// <summary>
    /// Write the runtime settings — the pricing knobs and the switches beside them.
    /// </summary>
    public const string SettingsWrite = "SettingsWrite";

    /// <summary>
    /// The staff operations that are none of the named capabilities: the error log, the feedback
    /// desk, an account's activation and its email address.
    /// </summary>
    public const string StaffOperations = "StaffOperations";

    /// <summary>
    /// Administer accounts: ban, unban, delete, and the assignment of a role. Admin and
    /// ProductOwner only — a Manager may activate a User account, but not mint or revoke a role.
    /// </summary>
    public const string UserAdministration = "UserAdministration";

    /// <summary>
    /// Turn the QA test-access switch. Admin and ProductOwner only: the switch decides whether a
    /// client-side test account may sign in at all, and the client-side test account is a
    /// <c>User</c> row - not a staff role.
    /// </summary>
    public const string QaTestAccess = "QaTestAccess";

    /// <summary>
    /// See a voucher's redeemable payload - the code the customer redeems at the pump.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The payload is a bearer instrument: whoever holds it can spend the customer's fuel, and
    /// nothing records that they did. That is the difference from a refund, which the Manager may
    /// make - a refund is an audited row and a ledger entry, and a redemption is neither.
    /// </para>
    /// <para>
    /// So the capability is its own, and it is narrower than the staff surface: an operator
    /// reads the stock, its status and its cost, and the code itself is for the Admin tier.
    /// </para>
    /// </remarks>
    public const string VoucherPayload = "VoucherPayload";

    /// <summary>
    /// See a customer's settlement code - the identifier the settlement channel resolves money to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code is financial data: it is what a message on the settlement channel resolves to a
    /// customer, and it is what an operator quotes to attribute a payment. It is not a bearer
    /// instrument - the channel is closed to the finance function - so it does not need the Admin
    /// tier the way a voucher's redeemable payload does.
    /// </para>
    /// <para>
    /// It is a capability of its own anyway, because the read surface must not decide it: a role
    /// that joins the staff surface joins the LISTS, and a list is not the same thing as money.
    /// The membership is a decision about money, so it is written down as one (planning #530, S-10).
    /// </para>
    /// </remarks>
    public const string SettlementCode = "SettlementCode";

    /// <summary>
    /// See a customer's handwritten signature - the image the customer drew on the contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A signature is the one artefact that cannot be rotated: the customer drew it once, it cannot
    /// be revoked, and a leaked copy of a person's handwriting is not repairable. So it is a
    /// capability of its own, narrower than the staff surface, the same way the redeemable voucher
    /// payload is (planning #622, S-13).
    /// </para>
    /// <para>
    /// The list behind it carries no image at all - the image is served per record, and opening it
    /// writes an audit row naming the operator. A capability that only says "may look" is a
    /// capability that nobody can answer for afterwards.
    /// </para>
    /// </remarks>
    public const string SignaturePayload = "SignaturePayload";
}
