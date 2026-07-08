using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Contracts.SharedModels;

public sealed class LegalEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = null!;
    public string Edrpou { get; set; } = null!;
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? DirectorName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public User? User { get; set; }
}

