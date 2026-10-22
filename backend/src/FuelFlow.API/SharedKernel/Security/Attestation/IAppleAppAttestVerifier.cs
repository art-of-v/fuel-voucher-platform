namespace FuelFlow.SharedKernel.Security.Attestation;

/// <summary>The server-owned key material from a verified App Attest attestation.</summary>
public sealed record AppleAttestationData(
    string KeyId,
    string PublicKey,
    long Counter,
    string Environment,
    string? Receipt);

/// <summary>The counter from a verified assertion, which is now the key's counter.</summary>
public sealed record AppleAssertionData(long Counter);

/// <summary>
/// Apple App Attest verification. The interface exists so the request pipeline can be tested
/// without Apple hardware or a real attestation object.
/// </summary>
public interface IAppleAppAttestVerifier
{
    /// <summary>Verifies the attestation and returns the key the server must store.</summary>
    /// <exception cref="AttestationVerificationException">The attestation is not acceptable.</exception>
    Task<AppleAttestationData> VerifyAttestationAsync(
        byte[] attestationObject,
        byte[] keyId,
        byte[] clientDataHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies one assertion. The key material is the server's stored copy, not the request's
    /// claim. Returns the new counter.
    /// </summary>
    /// <exception cref="AttestationVerificationException">The assertion is not acceptable.</exception>
    AppleAssertionData VerifyAssertion(
        byte[] assertion,
        byte[] clientDataHash,
        string keyId,
        string publicKey,
        long counter,
        string environment);
}
