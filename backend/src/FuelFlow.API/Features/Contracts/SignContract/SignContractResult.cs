namespace FuelFlow.Features.Contracts.SignContract;

/// <summary>
/// How a signature ended.
/// </summary>
public enum SignContractOutcome
{
    /// <summary>The signature was recorded.</summary>
    Done,

    /// <summary>The request did not pass the gate. The message says why.</summary>
    Invalid,

    /// <summary>The command carried no caller, so there was nobody to sign for.</summary>
    Unauthenticated,

    /// <summary>The contract is not the caller's.</summary>
    NotFound,

    /// <summary>The contract has already been signed.</summary>
    Conflict,
}

/// <summary>
/// The answer to a signature: the signature that was recorded, or the reason it was not.
/// </summary>
/// <param name="Outcome">How the signing ended.</param>
/// <param name="Signature">The signature, when it was recorded.</param>
/// <param name="Error">The reason it was not, when it was not.</param>
public sealed record SignContractResult(
    SignContractOutcome Outcome,
    ContractSignatureDto? Signature,
    string? Error)
{
    /// <summary>The signature was recorded.</summary>
    public static SignContractResult Done(ContractSignatureDto signature)
        => new(SignContractOutcome.Done, signature, null);

    /// <summary>The request was refused, and the message says why.</summary>
    public static SignContractResult Invalid(string error)
        => new(SignContractOutcome.Invalid, null, error);

    /// <summary>There was no caller to sign for.</summary>
    public static SignContractResult Unauthenticated()
        => new(SignContractOutcome.Unauthenticated, null, null);

    /// <summary>The contract is not the caller's.</summary>
    public static SignContractResult NotFound(string error)
        => new(SignContractOutcome.NotFound, null, error);

    /// <summary>The contract has already been signed.</summary>
    public static SignContractResult Conflict(string error)
        => new(SignContractOutcome.Conflict, null, error);
}
