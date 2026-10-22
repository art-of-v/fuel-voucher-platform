using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Auth.SharedModels;

/// <summary>
/// The second factor of a staff account: the TOTP secret bound to that account, and the
/// half-finished enrolment of one.
/// </summary>
/// <remarks>
/// <para>
/// One row per staff account. A second factor is bound to the <i>account</i>, not to the
/// device: the staff member's authenticator app is their own, and a second phone is enrolled
/// by scanning the same secret again - which is why revoking the factor removes the secret
/// rather than a device registration.
/// </para>
/// <para>
/// The secret is stored encrypted, never in the clear: a database dump must not be enough to
/// mint codes for the accounts it describes. The cipher is the same per-environment AES-256 key
/// the voucher payloads use (see <c>IQrCipher</c>) - a second key would be a second secret the
/// operator has to provision, and an unprovisioned secret is a sign-in nobody can complete.
/// </para>
/// <para>
/// The pending columns carry an enrolment that has not been proven yet. The server generates a
/// secret and hands it to the client; the client proves it by producing a code from it. Until
/// that happens the secret lives only in the pending columns and is overwritten by the next
/// attempt - an enrolment nobody finished must not become a factor nobody chose.
/// </para>
/// </remarks>
public sealed class StaffTwoFactor
{
    public Guid Id { get; set; }

    /// <summary>The staff account this factor belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The enrolled secret, encrypted. Null while the account has no second factor yet.
    /// </summary>
    public byte[]? SecretCiphertext { get; set; }

    /// <summary>
    /// A secret that was handed to a client and has not been proven yet, encrypted.
    /// </summary>
    public byte[]? PendingSecretCiphertext { get; set; }

    /// <summary>
    /// The hash of the outstanding challenge - the proof that the first factor was verified
    /// for this account and that a second factor may now be attempted.
    /// </summary>
    public string? PendingChallengeHash { get; set; }

    /// <summary>When the outstanding challenge stops being redeemable.</summary>
    public DateTime? PendingExpiresAtUtc { get; set; }

    /// <summary>
    /// How many times a code was tried against the outstanding challenge and failed.
    /// </summary>
    public int FailedAttempts { get; set; }

    /// <summary>When the row was created.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the row last changed.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>When a code was last accepted for this account.</summary>
    public DateTime? LastVerifiedAtUtc { get; set; }

    /// <summary>The staff account this factor belongs to.</summary>
    public User User { get; set; } = null!;
}
