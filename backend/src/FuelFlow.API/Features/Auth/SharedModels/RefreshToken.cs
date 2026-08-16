using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Auth.SharedModels;

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Groups every token produced by one login session. Rotation keeps the
    /// same family; if a revoked token is ever replayed the whole family is
    /// revoked (token-theft detection).
    /// </summary>
    public Guid FamilyId { get; set; }

    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public User User { get; set; } = null!;
}
