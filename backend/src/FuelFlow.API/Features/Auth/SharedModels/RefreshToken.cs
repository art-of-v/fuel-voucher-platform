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

    /// <summary>
    /// The device this refresh token belongs to. Null for legacy tokens
    /// created before device tracking was added. Enables device-specific logout.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// The role name captured when this session was created. Refresh uses THIS, not the
    /// user's current role, when minting access tokens — so promoting a user to Staff never
    /// silently upgrades an existing session on its next refresh (§16: new authorization
    /// applies only to a new session). A demotion instead revokes the session outright, so
    /// the snapshot only ever pins a session to a role no higher than the one it was born with.
    /// Copied forward unchanged on rotation. Null only for tokens minted before this column existed.
    /// </summary>
    public string? RoleNameAtIssue { get; set; }

    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public User User { get; set; } = null!;
}
