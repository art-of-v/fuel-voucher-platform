namespace FuelFlow.SharedKernel.Security.Attestation;

/// <summary>
/// The verdicts Play Integrity returned, or the reason the token was refused. Verdicts are carried
/// separately from the outcome so the caller can log them without logging the token.
/// </summary>
public sealed record PlayIntegrityResult(
    bool IsValid,
    string? Code = null,
    string? Detail = null,
    string? AppVerdict = null,
    string? DeviceVerdict = null,
    bool? IsTestingResponse = null)
{
    public static PlayIntegrityResult Valid(
        string? appVerdict,
        string? deviceVerdict,
        bool? isTestingResponse) =>
        new(true, AppVerdict: appVerdict, DeviceVerdict: deviceVerdict, IsTestingResponse: isTestingResponse);

    public static PlayIntegrityResult Invalid(
        string code,
        string detail,
        string? appVerdict = null,
        string? deviceVerdict = null,
        bool? isTestingResponse = null) =>
        new(false, code, detail, appVerdict, deviceVerdict, isTestingResponse);
}

/// <summary>
/// Google Play Integrity verification. Abstracted the same way as the Apple verifier: the API call
/// is the one part that cannot run in a unit test, and everything around it should be tested.
/// </summary>
public interface IPlayIntegrityVerifier
{
    /// <summary>
    /// Decodes and validates one integrity token. <paramref name="expectedRequestHash"/> is the
    /// base64url SHA-256 the client passed to <c>requestIntegrityCheck</c>.
    /// </summary>
    Task<PlayIntegrityResult> VerifyAsync(
        string integrityToken,
        string expectedRequestHash,
        CancellationToken cancellationToken);
}
