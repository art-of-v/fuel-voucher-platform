namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Canonical role identifiers and helpers.
/// </summary>
/// <remarks>
/// The numeric ranking — ProductOwner (100) &gt; Admin (50) &gt; Manager (10) &gt; User (0) —
/// lives in the <c>roles.level</c> column and is read from the database, not from a switch here.
/// This class holds only the names the application needs before a database round trip: the
/// authorization policies, and the singleton ProductOwner guard.
/// </remarks>
internal static class SeedRoles
{
    public const string ProductOwnerName = "ProductOwner";
    public const string AdminName = "Admin";
    public const string ManagerName = "Manager";
    public const string UserName = "User";

    /// <summary>
    /// A retired staff role. It is not in <see cref="StaffRoleNames"/>, so it admits nobody into
    /// the admin surface, and it is not a valid role name, so the application cannot hand it out.
    /// <para>
    /// <b>Why it is retired.</b> It used to sit between Manager and User and admitted a tester to
    /// every endpoint the Staff policy covers — including the ones that move money — while the
    /// QA identity the product actually needs is the client-side test account: an ordinary
    /// <c>User</c> row with <c>is_qa_account = true</c>, a sandboxed merchant and the QA stock
    /// pool (see <c>Features/Auth/QaTestAccess</c>). The owner confirmed on 2026-10-21 that no
    /// account holds the staff role, and that the two are not to be conflated.
    /// </para>
    /// <para>
    /// <b>Why the constant stays.</b> The row still exists in the <c>roles</c> table (migration
    /// <c>20261013093734_AddQaRole</c> seeded it), and a name the code no longer knows would make
    /// a row the database may still carry unnameable. Deleting the row is a separate, later
    /// cleanup; until then this constant is what says "retired, and deliberately so".
    /// </para>
    /// </remarks>
    public const string QaName = "QA";

    /// <summary>
    /// Every staff role, highest-first. Single source of truth for "who is staff" — used by
    /// IsStaff, the "Staff" authorization policy, and the admin SPA gate. Add a future staff
    /// role here once and it flows everywhere, instead of editing scattered role-name literals.
    /// <para>
    /// This list is the READ surface. A role added here may look at the staff surface and nothing
    /// more: the write capabilities are listed separately, each with its own members, so granting
    /// a role the ability to see the money is a second, deliberate step.
    /// </para>
    /// <para>
    /// Queries that need "is this user staff" answered by the DATABASE must reference this array
    /// rather than spelling the names out again: EF Core cannot translate the <see cref="IsStaff"/>
    /// method call into SQL, which is why earlier versions of those queries kept private copies of
    /// the list — and every copy silently omitted later roles, locking them out of the admin panel.
    /// </para>
    /// <para>
    /// <b>Money movement is here on purpose.</b> A refund, a payment placed for a customer and an
    /// order's status are staff actions, and the owner decided on 2026-10-21 that Manager has
    /// them alongside Admin and ProductOwner. What a Manager still cannot do is mint fuel that was
    /// not paid for: that is an invariant of the order, not of the role.
    /// </para>
    /// </remarks>
    public static readonly string[] StaffRoleNames = { ProductOwnerName, AdminName, ManagerName };

    /// <summary>
    /// Staff roles that may ban/unban/delete and assign a role (Manager excluded). Backs the
    /// <see cref="AuthPolicies.UserAdministration"/> and <see cref="AuthPolicies.QaTestAccess"/>
    /// policies.
    /// </summary>
    public static readonly string[] AdminAndOwnerRoleNames = { ProductOwnerName, AdminName };

    /// <summary>
    /// The roles that may write the product catalogue: the brands, their fuels, their stations,
    /// the fuel types, the prices, the suppliers and the contract texts.
    /// <para>
    /// <b>Why a list of its own.</b> Every write capability below carries the same three names
    /// today, and that is a decision, not a coincidence: the owner ruled on 2026-10-21 that a
    /// Manager's boundary is the catalogue, the voucher import, the settings and the money. They
    /// are still separate lists, because they are separate decisions — narrowing one capability
    /// (say, the settings) must not silently narrow the others, and a role that joins the staff
    /// surface to read must not thereby be able to move money.
    /// </para>
    /// </summary>
    public static readonly string[] CatalogueWriteRoleNames = { ProductOwnerName, AdminName, ManagerName };

    /// <summary>
    /// The roles that may write the voucher stock: the import and its batches, the operator's
    /// exchange and renewal, and the state of a voucher.
    /// </summary>
    public static readonly string[] VoucherWriteRoleNames = { ProductOwnerName, AdminName, ManagerName };

    /// <summary>
    /// The roles that may move money: a refund, a payment placed for a customer, an order's status.
    /// <para>
    /// A Manager has these, by the owner's decision of 2026-10-21. What no role has is the power
    /// to hand out fuel that was not paid for: that is an invariant of the order, and it lives with
    /// the order, not with the role.
    /// </para>
    /// </summary>
    public static readonly string[] MoneyMovementRoleNames = { ProductOwnerName, AdminName, ManagerName };

    /// <summary>
    /// The roles that may write the runtime settings.
    /// </summary>
    public static readonly string[] SettingsWriteRoleNames = { ProductOwnerName, AdminName, ManagerName };

    /// <summary>
    /// The roles that may run the staff operations that are none of the named capabilities: the
    /// error log, the feedback desk, an account's activation and its email address.
    /// </summary>
    public static readonly string[] StaffOperationsRoleNames = { ProductOwnerName, AdminName, ManagerName };

    /// <summary>
    /// The roles that may see a voucher's redeemable payload - the code the customer redeems at
    /// the pump.
    /// <para>
    /// <b>Why this is narrower than the staff surface.</b> The payload is a bearer instrument:
    /// whoever holds it can spend the customer's fuel, and nothing records that they did. A refund,
    /// which a Manager may make, is an audited row and a ledger entry - a redemption is neither.
    /// So the Admin tier holds the code, and the operations tier reads the stock, its status and
    /// its cost.
    /// </para>
    /// </summary>
    public static readonly string[] VoucherPayloadRoleNames = { ProductOwnerName, AdminName };

    /// <summary>
    /// The roles that may see a customer's settlement code - the identifier the settlement channel
    /// resolves money to.
    /// <para>
    /// <b>Why this is the money tier.</b> The code is not a profile field: it is what a message on
    /// the settlement channel is resolved BY, and it is what an operator quotes when they place a
    /// customer's payment. The owner decided on 2026-10-21 that placing a payment for a customer
    /// belongs to Admin, ProductOwner and Manager (planning #521, §7.1), and the settlement code is
    /// the identifier that placement is written against.
    /// </para>
    /// <para>
    /// <b>Why it is a list of its own.</b> It is listed here rather than riding on the staff read
    /// surface, so that a role joining the LISTS does not inherit every customer's code: reading
    /// the staff surface and being trusted with money are two separate decisions, and the second one
    /// is the one this list records.
    /// </para>
    /// </summary>
    public static readonly string[] SettlementCodeRoleNames = { ProductOwnerName, AdminName, ManagerName };

    // Fixed, deterministic GUIDs matching the seeded database rows.
    // Admin and User IDs must match existing seeded data (migrations 20260619000001, 20260915121550).
    // ProductOwner, Manager and QA are new roles; use stable random UUIDs.
    public static readonly Guid ProductOwnerId = Guid.Parse("2c4a8b1e-5f3d-4a7e-9c1b-8d2e6f4a3b5c");
    public static readonly Guid AdminRoleId    = Guid.Parse("0b6c503a-2086-4fe3-b617-b47385b474bd");
    public static readonly Guid ManagerRoleId  = Guid.Parse("3d5b9c2f-6a4e-4b8f-ad2c-9e3f7a5b6d8e");
    public static readonly Guid QaRoleId       = Guid.Parse("7d4a1e93-5c62-4b8f-9a03-e6d1c7b48215");
    public static readonly Guid UserRoleId     = Guid.Parse("1b445bd0-6f91-4b5a-ac2d-a9601691142f");

    /// <summary>
    /// Whether the application may hand this role to an account. The retired QA role is not on the
    /// list, so the answer is "no" for it: granting a role that grants nothing is how a person
    /// ends up with an account nobody can explain.
    /// </summary>
    public static bool IsValidRoleName(string? roleName) =>
        roleName == UserName || IsStaff(roleName);

    /// <summary>
    /// Whether the role is one of the staff roles. The retired QA role is not staff: it used to
    /// be, and while it was, it carried the admin surface - including the money-moving endpoints.
    /// </summary>
    public static bool IsStaff(string? roleName) =>
        roleName is not null && Array.IndexOf(StaffRoleNames, roleName) >= 0;
}
