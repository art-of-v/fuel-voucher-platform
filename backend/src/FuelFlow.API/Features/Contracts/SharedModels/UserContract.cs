using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Contracts.SharedModels;

public sealed class UserContract
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ContractId { get; set; }
    public string? SignatureData { get; set; }
    public DateTime SignedAtUtc { get; set; }

    /// <summary>
    /// The version of the text that was actually signed. The contract's own version is what it
    /// points at NOW; this is what the customer read at the moment they signed, and the two can
    /// differ - a new version of the terms does not rewrite a signature.
    /// </summary>
    public short TermsVersion { get; set; }

    public User? User { get; set; }
    public Contract? Contract { get; set; }
}
