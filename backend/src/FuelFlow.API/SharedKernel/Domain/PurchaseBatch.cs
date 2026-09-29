namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// The unit cost of one fuel type within one voucher import — партія = (ImportJobId × FuelTypeId).
/// Cost is stored as a rate (UAH per liter, denomination-independent); a voucher's cost is its
/// batch rate × liters (pricing epic slice 2a, spec §9).
/// </summary>
/// <remarks>
/// A row exists <b>only once the cost has been entered</b> — the absence of a batch row for an
/// (ImportJobId, FuelTypeId) pair means "no cost yet", which the activation gate refuses to sell.
/// <see cref="Provider"/> is denormalized for reporting; the join to <c>fuel_packages</c> is on
/// <see cref="FuelTypeId"/>, which already encodes supplier+fuel (e.g. "okko-dp").
/// </remarks>
public sealed class PurchaseBatch
{
    public Guid Id { get; set; }
    public Guid ImportJobId { get; set; }
    public string FuelTypeId { get; set; } = null!;

    /// <summary>Supplier station id (e.g. "okko"), denormalized from the batch's vouchers for reporting.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>Cost per liter in UAH (собівартість). Manual invoice entry in MVP; RQ-3 auto-parse deferred.</summary>
    public decimal CostPerLiter { get; set; }

    public Guid? EnteredByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
