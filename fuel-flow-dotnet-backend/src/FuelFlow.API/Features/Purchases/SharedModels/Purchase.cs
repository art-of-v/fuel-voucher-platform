using FuelFlow.Features.Auth.SharedModels;

namespace FuelFlow.Features.Purchases.SharedModels;

public sealed class Purchase
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    public User? User { get; set; }
}
