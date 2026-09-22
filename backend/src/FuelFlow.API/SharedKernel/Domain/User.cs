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

    public string? ReferralCode { get; set; }
    public string? ReferredBy { get; set; }
    public int BonusBalance { get; set; }

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
