using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Contracts.SharedModels;

public sealed class Contract
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid BrandId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the contract stops being valid. Mandatory since 2026-10-20, on the business's
    /// instruction: a contract without a term is not a contract, it is a record that something
    /// once happened.
    /// <para>
    /// The sign endpoint refuses to sign a contract that has already expired, so the term is
    /// not a field nobody reads - it is the thing that stops a signature.
    /// </para>
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    public User? User { get; set; }
    public LegalEntity? Entity { get; set; }
    public Brand? Brand { get; set; }
}
