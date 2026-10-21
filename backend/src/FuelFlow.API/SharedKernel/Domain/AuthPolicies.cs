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
    /// <c>User</c> row — not a staff role.
    /// </summary>
    public const string QaTestAccess = "QaTestAccess";
}
