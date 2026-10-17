namespace FuelFlow.SharedKernel.Domain;

public sealed class User
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid? RoleId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateOnly? Birthdate { get; set; }
    public string? ProfileImageUrl { get; set; }

    /// <summary>
    /// The user's party record (D-02, additive step 1). NULLABLE on purpose: the documented sequence
    /// is insert, switch, reconcile, then retire, and this column is the insert. Nothing reads it yet
    /// and no handler depends on it, so a user existing without one is not an error today - it becomes
    /// one when the column is promoted to NOT NULL, and that is a separate migration with a test that
    /// says so.
    /// <para>
    /// Back-filled for every existing user, and a BEFORE INSERT trigger creates one for every new user,
    /// so the column is only ever null for a row inserted between them.
    /// </para>
    /// </summary>
    public Guid? PartyId { get; set; }

    public string? ReferralCode { get; set; }
    public string? ReferredBy { get; set; }
    /// <summary>
    /// UAH. Decimal, not an integer type: money is never an integer type, whatever the scale - see the
    /// money-type decision in the schema notes. Displayed directly as UAH by the admin client
    /// (<c>admin.tsx</c> renders <c>{bonusBalance} UAH</c> with no division), so the stored unit and
    /// the displayed unit are the same and a /100 would be a bug.
    /// </summary>
    public decimal BonusBalance { get; set; }

    public bool IsActive { get; set; } = true;
    public int TokenVersion { get; set; } = 1;
    public bool IsDeleted { get; set; }
    public bool IsBanned { get; set; }

    /// <summary>
    /// A verified email change in flight (feature: admin email edit). An admin proposes a new
    /// address; it is held here (the active <see cref="Email"/> is untouched) until the recipient
    /// clicks the confirmation link, at which point it is promoted to <see cref="Email"/> and these
    /// are cleared. <see cref="PendingEmailTokenHash"/> is the SHA-256 of the one-time token (never
    /// the token itself); the request expires at <see cref="PendingEmailExpiresAtUtc"/>.
    /// </summary>
    public string? PendingEmail { get; set; }
    public string? PendingEmailTokenHash { get; set; }
    public DateTime? PendingEmailExpiresAtUtc { get; set; }

    /// <summary>
    /// Marks the single, seeded QA/App-Store-review test account. It exists so the QA identity is
    /// explicitly distinguishable from a real customer (least-privilege role <c>User</c>): the QA
    /// authentication path only ever mints/consumes codes for a phone that resolves to a row with
    /// this flag set, and disabling QA access targets exactly the accounts with this flag when
    /// revoking sessions. Never set on a customer account.
    /// </summary>
    public bool IsQaAccount { get; set; }

    public Role? Role { get; set; }
}
