using FuelFlow.Features.Auth.SharedModels;

namespace FuelFlow.Features.Contracts.SharedModels;

public sealed class UserContract
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ContractId { get; set; }
    public DateTime SignedAtUtc { get; set; }

    public User? User { get; set; }
    public Contract? Contract { get; set; }
}
