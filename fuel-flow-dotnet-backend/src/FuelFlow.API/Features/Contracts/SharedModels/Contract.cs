using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Stations.SharedModels;

namespace FuelFlow.Features.Contracts.SharedModels;

public sealed class Contract
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid LegalEntityId { get; set; }
    public string StationId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    public User? User { get; set; }
    public LegalEntity? Entity { get; set; }
    public Station? Station { get; set; }
}
