namespace FuelFlow.Features.Auth.SharedModels;

public sealed class VerificationCode
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}
