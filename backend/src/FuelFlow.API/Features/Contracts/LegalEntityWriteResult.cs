namespace FuelFlow.Features.Contracts;

/// <summary>
/// How a legal-entity write ended.
/// </summary>
public enum LegalEntityWriteOutcome
{
    /// <summary>The company was written.</summary>
    Done,

    /// <summary>The details did not pass the gate. The message says why.</summary>
    Invalid,

    /// <summary>The command carried no caller, so there was nobody to write for.</summary>
    Unauthenticated,

    /// <summary>The EDRPOU belongs to somebody else.</summary>
    Conflict,

    /// <summary>The company is not the caller's.</summary>
    NotFound,
}

/// <summary>
/// The answer to a legal-entity write: the company as it now stands, or the reason it was not
/// written.
/// </summary>
/// <param name="Outcome">How the write ended.</param>
/// <param name="Entity">The company, when it was written.</param>
/// <param name="Error">The reason it was not, when it was not.</param>
public sealed record LegalEntityWriteResult(
    LegalEntityWriteOutcome Outcome,
    LegalEntityDto? Entity,
    string? Error)
{
    /// <summary>The company was written.</summary>
    public static LegalEntityWriteResult Done(LegalEntityDto entity)
        => new(LegalEntityWriteOutcome.Done, entity, null);

    /// <summary>The details were refused, and the message says why.</summary>
    public static LegalEntityWriteResult Invalid(string error)
        => new(LegalEntityWriteOutcome.Invalid, null, error);

    /// <summary>There was no caller to write for.</summary>
    public static LegalEntityWriteResult Unauthenticated()
        => new(LegalEntityWriteOutcome.Unauthenticated, null, null);

    /// <summary>The EDRPOU belongs to somebody else.</summary>
    public static LegalEntityWriteResult Conflict(string error)
        => new(LegalEntityWriteOutcome.Conflict, null, error);

    /// <summary>The company is not the caller's.</summary>
    public static LegalEntityWriteResult NotFound(string error)
        => new(LegalEntityWriteOutcome.NotFound, null, error);
}
