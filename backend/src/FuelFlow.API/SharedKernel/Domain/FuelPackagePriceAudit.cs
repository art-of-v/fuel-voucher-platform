namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelPackagePriceAudit
{
    public Guid Id { get; set; }
    public string PackageId { get; set; } = null!;
    public string FuelName { get; set; } = null!;

    public decimal? OldSupplierPricePerLiter { get; set; }
    public decimal? NewSupplierPricePerLiter { get; set; }

    public decimal? OldMarginUahPerLiter { get; set; }
    public decimal? NewMarginUahPerLiter { get; set; }

    public decimal? OldMarginPercent { get; set; }
    public decimal? NewMarginPercent { get; set; }

    public decimal? OldFinalPricePerLiter { get; set; }
    public decimal? NewFinalPricePerLiter { get; set; }

    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
