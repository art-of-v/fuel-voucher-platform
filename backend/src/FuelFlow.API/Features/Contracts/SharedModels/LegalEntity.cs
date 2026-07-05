namespace FuelFlow.Features.Contracts.SharedModels;

public sealed class LegalEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
}
