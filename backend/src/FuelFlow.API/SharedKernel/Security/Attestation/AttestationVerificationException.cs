namespace FuelFlow.SharedKernel.Security.Attestation;

/// <summary>
/// A proof could not be verified. The code is what the API returns; the message is for the log,
/// not for the caller's body verbatim - the caller gets a stable sentence from the service.
/// </summary>
public sealed class AttestationVerificationException : Exception
{
    public AttestationVerificationException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
