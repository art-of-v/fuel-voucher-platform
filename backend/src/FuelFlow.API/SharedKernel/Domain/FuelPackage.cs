using System.ComponentModel.DataAnnotations.Schema;
namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelPackage
{
    public string Id { get; set; } = null!;
    public string BrandId { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }

/// <summary>
/// What this nominal costs, effective: the fuel's rate x these litres, capped by the ceiling.
/// Computed by <see cref="FuelPricing.PackagePrice"/>, never stored. Zero when the fuel is not
/// priced yet — the admin listing still shows the nominal exists, the storefront skips it and
/// checkout refuses it.
/// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// The struck "before" price: the pump price this size would otherwise sell at, so the
    /// storefront can show what the customer saves.
    /// </summary>
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
    /// Which pool this package belongs to: <see cref="DataKinds.Real"/> or <see cref="DataKinds.Qa"/>.
    /// </summary>
    /// <remarks>
    /// Replaces the <c>is_test_data boolean</c> this used to be. The story of that flag is in
    /// <see cref="DataKinds"/>: production absorbed two rehearsal packages published as live, and
    /// nothing in the schema could object, because a boolean cannot express "nobody said".
    /// <para>
    /// Not the same thing as <see cref="FuelVoucher.DataKind"/>. On a voucher the value means
    /// "emulated: this code does not exist at the Brand". Here it means "do not sell this". A
    /// package is a catalogue entry, not paper, so it needs its own name for its own rule - but both
    /// now draw the line in the same place.
    /// </para>
    /// <para>
    /// Deliberately not a global query filter. A filter would also hide the row from the admin
    /// screens and from anything that resolves an order line to the package it was bought against,
    /// which would turn a rehearsal into an invisible dangling reference. Only the public handler
    /// filters on the pool.
    /// </para>
    /// </remarks>
    public string DataKind { get; set; } = DataKinds.Real;

    /// <summary>
    /// Whether this package is rehearsal data and must never reach the storefront.
    /// </summary>
    /// <remarks>
    /// A view over <see cref="DataKind"/>, not a column. It exists so the ~20 read sites that ask
    /// "is this the QA pool?" keep reading as a question about a pool rather than about storage,
    /// while the column itself is a named value the database can constrain. Assigning it still works
    /// and is how the admin write paths state their intent.
    /// </remarks>
    [NotMapped]
    public bool IsTestData
    {
        get => DataKind == DataKinds.Qa;
        set => DataKind = value ? DataKinds.Qa : DataKinds.Real;
    }
}
