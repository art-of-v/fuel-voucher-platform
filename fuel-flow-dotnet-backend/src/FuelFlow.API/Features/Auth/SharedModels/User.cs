namespace FuelFlow.Features.Auth.SharedModels;

public sealed class User
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public Guid? RoleId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Referral system
    public string? ReferralCode { get; set; }
    public string? ReferredBy { get; set; }
    public int BonusBalance { get; set; }

    public Role? Role { get; set; }
}
