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
    /// Marks the single, seeded QA/App-Store-review test account. It exists so the QA identity is
    /// explicitly distinguishable from a real customer (least-privilege role <c>User</c>): the QA
    /// authentication path only ever mints/consumes codes for a phone that resolves to a row with
    /// this flag set, and disabling QA access targets exactly the accounts with this flag when
    /// revoking sessions. Never set on a customer account.
    /// </summary>
    public bool IsQaAccount { get; set; }

    public Role? Role { get; set; }
}
