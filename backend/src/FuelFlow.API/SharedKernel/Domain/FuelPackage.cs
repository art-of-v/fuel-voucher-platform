namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelPackage
{
    public string Id { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }
    public decimal Price { get; set; }
    public decimal OriginalPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Per-liter pricing (new pricing system)
    public decimal? SupplierPricePerLiter { get; set; }   // cost/л (собівартість)
    public decimal? MarginUahPerLiter { get; set; }       // profit/л (заробіток)
    public decimal? MarginPercent { get; set; }
    public decimal? FinalPricePerLiter { get; set; }      // computed: see FuelPricing.FinalPerLiter

    // Slice 1a pricing knobs. Pump (колонка) is the ceiling; MinDiscount is the
    // guaranteed gap under it. final = min(cost + profit, pump − minDiscount).
    // Pump null ⇒ fall back to cost + profit (legacy cost-plus).
    public decimal? PumpPricePerLiter { get; set; }
    public decimal? MinDiscountPerLiter { get; set; }

    public DateTime? PriceUpdatedAt { get; set; }
    public Guid? PriceUpdatedByUserId { get; set; }

    /// <summary>
    /// Archive, not delete. Deleting a catalog entry that fuel has already been sold against would
    /// cascade into the vouchers - real, paid-for paper - and destroy the only record of what a
    /// customer bought (#217). A used entry is soft-deleted: hidden from the catalog, still there for
    /// reports and for every historical row that points at it.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>When the entry was archived, or null while it is live.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>
    /// This package is not real stock: it exists so someone can rehearse a flow without spending
    /// money, and it must never reach the storefront.
    ///
    /// Added after production served a QA fuel type as a live package - a 5 L "QA-CHECK-95" at
    /// 1 UAH, purchasable through the same anonymous endpoint the app reads. The cleanup deleted
    /// the rows; this flag is what stops the next one from being sellable.
    ///
    /// Not the same thing as <see cref="FuelVoucher.IsTestData"/>. On a voucher the flag means
    /// "emulated: this code does not exist at the station". Here it means "do not sell this". A
    /// package is a catalogue entry, not paper, so it needs its own name for its own rule.
    ///
    /// Deliberately not a global query filter. A filter would also hide the row from the admin
    /// screens and from anything that resolves an order line to the package it was bought against,
    /// which would turn a rehearsal into an invisible dangling reference. Only the public handler
    /// filters on it.
    /// </summary>
    public bool IsTestData { get; set; }
}
