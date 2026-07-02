namespace FuelFlow.Features.Vouchers.SharedModels;

public sealed class Voucher
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelType { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid? AssignedToUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
