namespace FuelFlow.Features.Stations.SharedModels;

public sealed class PriceChangeAudit
{
    public Guid Id { get; set; }
    public string FuelTypeId { get; set; } = null!;
    public int OldBasePrice { get; set; }
    public int NewBasePrice { get; set; }
    public int OldDiscountPrice { get; set; }
    public int NewDiscountPrice { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
