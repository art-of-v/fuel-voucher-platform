using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Company.SharedModels;

public sealed class CompanyInvitation
{
    public Guid Id { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string WorkerPhoneNumber { get; set; } = null!;
    public Guid WorkerUserId { get; set; }
    public InvitationStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public LegalEntity LegalEntity { get; set; } = null!;
    public User OwnerUser { get; set; } = null!;
    public User WorkerUser { get; set; } = null!;
}
