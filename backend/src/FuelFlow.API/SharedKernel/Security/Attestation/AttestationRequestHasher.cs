namespace FuelFlow.SharedKernel.Security.Attestation;

/// <summary>
/// The one canonical encoding of "this exact request" that the client and the server both hash.
/// </summary>
/// <remarks>
/// <para>
/// The hash binds an attestation challenge to a method, a path and the exact body the caller is
/// about to send. Without it a challenge obtained for a harmless request could be attached to a
/// checkout, which is the whole point of asking for a challenge in the first place.
/// </para>
/// <para>
/// The body is the JSON string as it goes on the wire, not a re-serialised object: two
/// serialisations can differ in key order or whitespace, and then the two sides hash different
/// bytes and every proof fails. The mobile client hashes <c>options.body</c>; the server hashes
/// the buffered request body. The path is the path the client requested, without the query string.
/// </para>
/// </remarks>
public static class AttestationRequestHasher
{
    public static string Compute(string method, string path, string body)
    {
        var canonical = $"{method.ToUpperInvariant()}\n{path}\n{body}";
        return AttestationEncoding.ToBase64Url(AttestationEncoding.Sha256(canonical));
    }

    /// <summary>
    /// The bytes each provider signs for one request: SHA-256 of <c>challenge:requestHash</c>.
    /// </summary>
    /// <remarks>
    /// Apple's App Attest hashes the string passed to <c>generateAssertion</c> itself, so the client
    /// passes this exact string and the server computes the same digest. Google Play Integrity takes
    /// the digest itself as <c>requestHash</c>, so the client passes
    /// <see cref="ComputeProofRequestHash"/> and the server compares it to the token payload.
    /// </remarks>
    public static byte[] ComputeClientDataHash(string challenge, string requestHash) =>
        AttestationEncoding.Sha256($"{challenge}:{requestHash}");

    public static string ComputeProofRequestHash(string challenge, string requestHash) =>
        AttestationEncoding.ToBase64Url(ComputeClientDataHash(challenge, requestHash));
}
