namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelTypeEntity
{
    /// <summary>
    /// The fuel's identity: a uuid, since planning D-21.
    /// </summary>
    /// <remarks>
    /// It used to be the natural key - <c>"okko-95"</c> - and the brand's own code for the grade lives
    /// in <see cref="BrandFuelCode"/> instead. A uuid column is what lets the tables pointing at a fuel
    /// carry a real foreign key, which is what D-02 has been waiting for.
    /// </remarks>
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid BrandId { get; set; }

    /// <summary>
    /// The grade's code in the <b>brand's</b> own spelling, as the brand publishes it on its price
    /// sheet: <c>"A-95"</c>, <c>"DP"</c>, <c>"Pulls 95"</c>, <c>"SPBT"</c> for OKKO. Nullable,
    /// because a fuel may exist without one - a hand-entered grade, a rehearsal fuel - and unique per
    /// brand among live rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The brand's code, not the supplier's.</b> Those are different things here and the column name
    /// says which: a <b>brand</b> is a network (<c>Brands</c>: okko, wog, klo) whose fuel we sell,
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
    /// This code - not the id, and not <see cref="Name"/> - is the half of the fuel's identity that
    /// an external party can state: the brand's price sheet names every fuel it sells, in the brand's
    /// own spelling, and that is what an import of the catalogue matches on. It is not a name - "А-95"
    /// and "A-95" are the same fuel and two different strings, which no index on
    /// <see cref="Name"/> can reconcile: the name is folded onto one spelling by
    /// <see cref="FuelGrade"/>, and it is the folded name that <c>fuel_types.grade_key</c> holds and
    /// that <c>ux_fuel_types_brand_grade_key</c> refuses a duplicate of. The QR product codes the
    /// voucher side uses (9018, 45290) are a <em>second</em> crosswalk to the same grades and are
    /// deliberately not stored here; one grade has several product codes and several codes can name
    /// one grade.
    ///
    /// <para>
    /// Planning #349 settled on 2026-10-19 that <see cref="Id"/> is a generated uuid and carries no
    /// meaning - so this is the stable half, and the id never was one except in the rows
    /// <c>20260618224553_AddStationCatalog</c> seeded, which is every row a freshly migrated
    /// database has and none of the 28 on production. An importer resolves a PDF row to this code;
    /// it never parses an id.
    /// </para>
    /// <para>
    /// Nothing writes it automatically yet, and nothing derives it from <see cref="Name"/>: guessing a
    /// brand's code from a fuel name is the fragility this column exists to remove. It is filled in by
    /// hand today and by the catalogue import when that lands.
    /// </para>
    /// </remarks>
    public string? BrandFuelCode { get; set; }

    /// <summary>
    /// The list price of the fuel per litre - the price before our discount. It is the reference
    /// the customer's saving is measured against. NULL means it is not set: the reference is the
    /// COLUMN price, and nobody has recorded a pump price for this fuel.
    /// </summary>
    /// <remarks>
    /// A copy, and the copy is what this whole change is about: the reference is the pump price
    /// next door, and the customer's price is computed from the knobs. Kept as a column until the
    /// migration that drops it, and nothing writes it any more.
    /// </remarks>
    public decimal? BasePrice { get; set; }

    /// <summary>
    /// What the customer pays per litre. NULL means it is not set: the grade is in the
    /// catalogue and nobody has priced it yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A price of zero is a price, and a price of zero means the customer takes the fuel for
    /// nothing. So "not priced yet" is NULL and never 0 - the same way the neighbouring cost
    /// says it, and the reason this column is nullable at all.
    /// </para>
    /// <para>
    /// The catalogue import is what leaves a grade unpriced, and it leaves it as NULL: the pump
    /// publishes a price, the cost and the margin are not known, and until an operator sets a
    /// margin there is no customer price to quote. A grade with no price is not on the shelf -
    /// its computed price is null and the storefront does not list a package with no price - so
    /// nothing reaches a customer at 0.
    /// </para>
    /// <para>
    /// A copy, and the copy is what this whole change is about: the price the customer pays is
    /// computed from the knobs by <see cref="FinalPerLiter"/>, and four different writers kept
    /// this column in step with it by hand. Kept as a column until the migration that drops it,
    /// and nothing writes it any more.
    /// </para>
    /// </remarks>
    public decimal? DiscountPrice { get; set; }

    /// <summary>
    /// Manager standing opt-in: allow this brand+fuel to be priced and sold BELOW blended
    /// cost (deliberate loss-leader). Default false, so the slice-3 hard block refuses below-cost
    /// pricing/sales until a manager consciously flips it. A per brand+fuel decision — the fuel type
    /// row already belongs to one brand (<c>brand_id</c>), and <see cref="BrandFuelCode"/> is that
    /// brand's own code for the grade.
    /// </summary>
    public bool AllowBelowCost { get; set; }

    /// <summary>
    /// What we add per litre on top of what we paid, and the only knob of the customer price an
    /// operator sets directly. The customer price is <c>cost + this</c> — the column price is the
    /// reference the customer is shown against, not a term of the price, and there is no second
    /// discount knob, because two caps on one number is one too many.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why it lives here and not on the offer.</b> A margin is a fact about the fuel, not about a
    /// sale of it: OKKO's diesel carries the same margin whether we hold it as stock, sell five litres
    /// or hundred. While it lived on <c>fuel_offers</c> it was per (brand × fuel × pool), which meant
    /// two places to set one number and a price that could differ depending on which pool answered —
    /// the same class of defect as two rows for one grade.
    /// </para>
    /// <para>
    /// <b>Nullable, and null means "not priced yet".</b> A fuel with a cost but no margin has no
    /// customer price and must show none: treating the missing margin as zero would publish a free
    /// fuel. That is also what the shelf reads to decide between the pump price and a real price.
    /// </para>
    /// <para>
    /// The cost it is added to is derived, never stored here — it is the litres-weighted average of
    /// unspent cost-bearing vouchers (CODEBASE.md in the private docs repo, <c>purchase_batches</c>,
    /// planning #360).
    /// </para>
    /// </remarks>
    public decimal? MarginUahPerLiter { get; set; }

    /// <summary>
    /// What we pay for the litre, or null while nobody has recorded a cost. The fuel's, not a
    /// per-package copy: four copies of one number across the nominals is how they drift.
    /// </summary>
    /// <remarks>
    /// Moved here from <c>fuel_offers.supplier_price_per_liter</c> on 2026-10-19. The offer existed
    /// so that two pools could hold two prices for one grade; with the pool moved onto the size list
    /// and the margin already here, the offer was a second place to say what a litre costs.
    /// </remarks>
    public decimal? SupplierPricePerLiter { get; set; }

    /// <summary>
    /// The COLUMN price: what the network charges at the pump, and the reference the customer's
    /// saving is measured against. It is not a term of our price - our price is the cost plus the
    /// margin - and it can sit below what we charge. Null means the market price is not known.
    /// </summary>
    public decimal? PumpPricePerLiter { get; set; }

    /// <summary>
    /// The margin as a percentage, kept beside the per-litre figure it is shown next to. Only ever
    /// read; the price is computed from <see cref="MarginUahPerLiter"/> because a percentage of a
    /// price that is itself a function of the margin is circular.
    /// </summary>
    public decimal? MarginPercent { get; set; }

    /// <summary>
    /// The customer price per litre these knobs produce, or <c>null</c> while no cost or no margin is
    /// recorded.
    /// <para>
    /// A method, not a property, and that is the whole point: it was a property on the offer reading a
    /// margin column nothing wrote. Every knob it needs lives here, so there is no argument left to get
    /// in the wrong order at a call site - the one failure mode that shape invited.
    /// </para>
    /// </summary>
    public decimal? FinalPerLiter() =>
        FuelPricing.FinalPerLiterOrNull(
            SupplierPricePerLiter, MarginUahPerLiter);

    /// <summary>
    /// When this fuel was last repriced, and by whom - stamped whether or not the number came out
    /// different. Moved here from the offer on 2026-10-19 with the rest of the pricing.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="UpdatedAtUtc"/>, which moves for any change at all. "The price was
    /// touched" is the question a manager asks after a reprice that turned out to be a no-op, and
    /// <c>updated_at</c> cannot answer it.
    /// </remarks>
    public DateTime? PriceUpdatedAt { get; set; }

    public Guid? PriceUpdatedByUserId { get; set; }

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
