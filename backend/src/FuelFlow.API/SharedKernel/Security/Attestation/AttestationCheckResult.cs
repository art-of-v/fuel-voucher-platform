namespace FuelFlow.SharedKernel.Security.Attestation;

public enum AttestationStatus
{
    /// <summary>The proof was present and valid.</summary>
    Valid = 0,

    /// <summary>The request carried no attestation headers at all.</summary>
    Missing = 1,

    /// <summary>A proof was present but could not be accepted.</summary>
    Invalid = 2,

    /// <summary>The verifier or the challenge store is unavailable. This is not the client's fault.</summary>
    Unavailable = 3,
}

/// <summary>
/// The outcome of checking one request's proof. <see cref="Code"/> is the stable machine code the
/// middleware writes into the error envelope; <see cref="Detail"/> is the sentence for a human.
/// </summary>
public sealed record AttestationCheckResult(
    AttestationStatus Status,
    string? Code = null,
    string? Detail = null)
{
    public bool IsValid => Status == AttestationStatus.Valid;

    public static AttestationCheckResult Valid() => new(AttestationStatus.Valid);

    public static AttestationCheckResult Missing() =>
        new(AttestationStatus.Missing, "ATTESTATION_REQUIRED",
            "This endpoint requires a valid app-attestation proof. Update the app and try again.");

    public static AttestationCheckResult Invalid(string code, string detail) =>
        new(AttestationStatus.Invalid, code, detail);

    public static AttestationCheckResult Unavailable(string detail) =>
        new(AttestationStatus.Unavailable, "ATTESTATION_UNAVAILABLE", detail);
}
