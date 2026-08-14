using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Company.SharedModels;

public sealed class CompanyMember
{
    public Guid Id { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid WorkerUserId { get; set; }
    public DateTime JoinedAtUtc { get; set; }

    public LegalEntity LegalEntity { get; set; } = null!;
    public User WorkerUser { get; set; } = null!;
}
