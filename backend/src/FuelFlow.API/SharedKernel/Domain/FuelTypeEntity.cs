namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelTypeEntity
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string StationId { get; set; } = null!;

    /// <summary>
    /// The grade's code in the <b>brand's</b> own spelling, as the brand publishes it on its price
    /// sheet: <c>"A-95"</c>, <c>"DP"</c>, <c>"Pulls 95"</c>, <c>"SPBT"</c> for OKKO. Nullable,
    /// because a fuel may exist without one - a hand-entered grade, a rehearsal fuel - and unique per
    /// brand among live rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The brand's code, not the supplier's.</b> Those are different things here and the column name
    /// says which: a <b>brand</b> is a network (<c>stations</c>: okko, wog, klo) whose fuel we sell,
    /// and a <b>supplier</b> is a third party we buy that fuel from (<c>suppliers</c>,
    /// <c>purchase_batches.supplier_id</c>, <c>supplier_price_per_liter</c> - our cost). One brand's
    /// fuel can come from many suppliers, so a supplier cannot identify a grade and this code is
    /// nobody's to choose. It is the brand's.
    /// </para>
    /// <para>
    /// <b>What this is for.</b> <see cref="Id"/> stopped being the fuel's name when the write path
    /// began generating it, and the name was the only thing identifying a fuel that the system agreed
    /// on: the voucher importer resolves one by <c>FirstOrDefault</c> over its name, so a second
    /// "А-95" at the same brand used to be impossible (the id collided) and then became a second offer,
    /// one fuel on the storefront at two prices, and a voucher that could be priced against whichever
    /// twin the importer picked.
    /// </para>
    /// <para>
    /// The code is the stable half of the fuel's identity, and the half the brand can be asked for: its
    /// price sheet names every fuel it sells. It is what an import of the catalogue matches on, and it
    /// is not a name - "А-95" and "A-95" are the same fuel and two different strings, which no index
    /// on <see cref="Name"/> can reconcile. The QR product codes the voucher side uses (9018, 45290)
    /// are a <em>second</em> crosswalk to the same grades and are deliberately not stored here; one
    /// grade has several product codes and several codes can name one grade.
    /// </para>
    /// <para>
    /// Nothing writes it automatically yet, and nothing derives it from <see cref="Name"/>: guessing a
    /// brand's code from a fuel name is the fragility this column exists to remove. It is filled in by
    /// hand today and by the catalogue import when that lands.
    /// </para>
    /// </remarks>
    public string? BrandFuelCode { get; set; }
    public decimal BasePrice { get; set; }
    public decimal DiscountPrice { get; set; }

    /// <summary>
    /// Manager standing opt-in: allow this brand+fuel to be priced and sold BELOW blended
    /// cost (deliberate loss-leader). Default false, so the slice-3 hard block refuses below-cost
    /// pricing/sales until a manager consciously flips it. A per brand+fuel decision — the fuel type
    /// row already belongs to one brand (<c>station_id</c>), and <see cref="BrandFuelCode"/> is that
    /// brand's own code for the grade.
    /// </summary>
    public bool AllowBelowCost { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

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
    /// A brand fuel code as this system stores it: trimmed, and empty means <em>no code</em> rather
    /// than an empty string. One rule for every write path, because "" and null mean opposite things
    /// to a unique index — null is excluded from it, "" is a value.
    /// </summary>
    public static string? NormaliseBrandFuelCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim();
}
