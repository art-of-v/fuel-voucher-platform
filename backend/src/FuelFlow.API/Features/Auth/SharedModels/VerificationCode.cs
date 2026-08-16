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

    /// <summary>
    /// Number of failed verification attempts against this code. The code is
    /// invalidated once the limit is reached, so a single code cannot be
    /// brute-forced even if IP-based rate limiting is bypassed.
    /// </summary>
    public int FailedAttempts { get; set; }
}
