namespace FuelFlow.Features.Attestation.SharedModels;

/// <summary>
/// One Apple App Attest key that the server has verified and now accepts assertions from.
/// </summary>
/// <remarks>
/// The key is bound to the device that registered it, not to the user. A user can sign in on a
/// different device, and a device can change hands; binding the key to the device id keeps the
/// attestation independent of the account lifecycle, so logout does not delete the key and a
/// replaced session does not force a new Apple attestation. The device-signature middleware still
/// binds the device to the authenticated user for the protected money endpoints.
/// </remarks>
public sealed class AttestationKey
{
    public Guid Id { get; set; }

    /// <summary>The App Attest key id, base64. The client sends this on every assertion.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The device that registered the key, as the app reports it.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// The public key from the verified attestation, base64. It is the key the assertion
    /// signature is checked against, so it is server-owned data rather than a client claim.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// The last accepted assertion counter. App Attest counters only move forward; the next
    /// assertion must be greater, which is what makes a captured assertion single-use.
    /// </summary>
    public long Counter { get; set; }

    /// <summary>The App Attest environment the key was attested in.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// The receipt from the attestation, when the verifier supplies one. Kept for audit and for a
    /// future server-to-server risk assessment; it is not needed to verify an assertion.
    /// </summary>
    public string? Receipt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastUsedAt { get; set; }

    /// <summary>Set when an operator revokes a key; a revoked key is never accepted again.</summary>
    public DateTime? RevokedAt { get; set; }
}
