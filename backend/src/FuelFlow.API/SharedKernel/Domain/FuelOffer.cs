namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// One commercial offer: a fuel sold by one Brand, priced per litre, offered in several volumes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this table exists.</b> Before it, <c>fuel_packages</c> modelled a <em>volume</em> as a
/// product: a row per (Brand, fuel type, litres), each row carrying its own copy of the per-litre
/// pricing. A95 Euro at OKKO is one offer sold in 2/3/5/10/20/50 L, so the six per-litre facts were
/// stored six times. Measured on production 2026-10-14: 13 offers producing 58 live rows, with
/// <c>final_price_per_liter</c> varying across volumes in <b>0</b> of them — one fact, 58 copies.
/// </para>
///
/// <para>
/// Duplication on a money row is not a style problem, it is a correctness one. Changing a margin has
/// to update every volume row, six copies can disagree, and nothing detects it. The storefront
/// headline is the minimum over the volumes' per-litre prices
/// (<c>website/src/lib/catalogue.ts:113</c>), so <b>one drifted copy becomes the published price of the
/// brand</b> — which is exactly the mechanism that put 1,00 грн/літр on the live site. See planning
/// #318, finding F-33.
/// </para>
///
/// <para>
/// The volume is not gone, it is an attribute: see <c>FuelOfferVolume</c>. Offer-level facts live
/// here once; the multiplication to litres happens at read time through
/// <see cref="FuelPricing.OriginalPackagePrice"/>.
/// </para>
///
/// <para>
/// <b>Nothing references a package row.</b> <c>order_line_items</c> carries
/// <c>Brand + FuelTypeId + Liters</c> and no <c>PackageId</c>, and there is no foreign key to
/// <c>fuel_packages</c> anywhere in the schema. So an offer can be renamed or re-keyed freely; a
/// volume row keeps the id of the package row it replaces, so any id quoted in a log or a support
/// ticket still resolves to the same thing after the split.
/// </para>
/// </remarks>
public sealed class FuelOffer
{
    /// <summary>
    /// Key of the offer: its <see cref="FuelTypeId"/>, with <c>-qa</c> appended for the rehearsal
    /// pool, derived by <see cref="OfferKeys"/>. The Brand is deliberately not spelled out — a fuel
    /// type belongs to exactly one Brand, so its id already determines it, and writing both produced
    /// <c>okko-okko-95</c>. Bounded by CHECK at 103 rather than at the 100 the single-column keys use.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// The network selling it. Mirrors <c>fuel_types.brand_id</c> — which is what lets the id above
    /// omit it — and is carried separately because the storefront groups and resolves on the pair: a
    /// fuel type that a supplier lists under two networks must be able to say so.
    /// <c>deploy/preflight-db/012</c> reports the case where the two disagree.
    /// </summary>
    public Guid BrandId { get; set; }

    /// <summary>Natural key of the fuel, as read from supplier PDFs. See <c>fuel_types.id</c>.</summary>
    public string FuelTypeId { get; set; } = null!;

    /// <summary>
    /// The fuel this offer sells, so a read path can take the margin from
    /// <see cref="FuelTypeEntity.MarginUahPerLiter"/> — the fuel, not the offer, is where a margin
    /// lives (planning #360).
    /// <para>
    /// Optional navigation, so a query that does not need the fuel does not drag the join in with it.
    /// It also means the fuel's own global filter applies inside that join, which is the point: an
    /// archived fuel leaves the shelf. That costs nothing, because a fuel may only be archived once
    /// every one of its vouchers is spent or expired.
    /// </para>
    /// </summary>
    public FuelTypeEntity? FuelType { get; set; }

    // Per-litre pricing. One copy per offer — this is the whole point of the table.
    //
    // Inputs only, since 2026-10-17: `final_price_per_liter` is gone, because it was
    // `min(cost + profit, pump − minDiscount)` written down next to the three columns it is
    // computed from, and every write path computed it exactly that way before storing it.
    // Measured on production the day it was dropped: 25/25 live offers agreed with the
    // arithmetic, and the only two write paths that could have made the stored copy differ
    // were the two that took a final price from the request body and wrote whatever the caller
    // sent — which is not a price, it is a request. Reads call
    // `FuelPricing.FinalPerLiterOrNull`, or the [NotMapped] property below.
    public decimal? SupplierPricePerLiter { get; set; }   // cost/л (собівартість)

    /// <summary>
    /// The offer's own copy of the margin. <b>Nothing writes or reads it any more</b> — the margin
    /// belongs to <see cref="FuelTypeEntity.MarginUahPerLiter"/> and every read path has moved there.
    /// The column goes in the migration that follows; the property stays until then so the entity and
    /// that migration agree on the shape of the table.
    /// </summary>
    public decimal? MarginUahPerLiter { get; set; }       // profit/л (заробіток)
    public decimal? MarginPercent { get; set; }

    // Slice 1a pricing knobs. Pump (колонка) is the ceiling; MinDiscount is the
    // guaranteed gap under it. final = min(cost + profit, pump − minDiscount).
    // Pump null ⇒ fall back to cost + profit (legacy cost-plus).
    public decimal? PumpPricePerLiter { get; set; }
    public decimal? MinDiscountPerLiter { get; set; }

    /// <summary>
    /// The customer price per litre this offer's knobs produce once the margin is supplied, or
    /// <c>null</c> while no cost or no margin is recorded.
    /// <para>
    /// <b>A method, not a property, and that is the whole point.</b> This was
    /// <c>ComputedFinalPricePerLiter</c>, reading <see cref="MarginUahPerLiter"/> itself — which is
    /// correct only while the offer holds a margin that something writes. It no longer does, so that
    /// property would have answered cost-plus-nothing on every read path that had not moved yet, and
    /// every path that had would have had to remember to derive the number itself and get the argument
    /// order right. Taking the margin as an argument is the one shape that cannot be called wrong.
    /// </para>
    /// </summary>
    public decimal? FinalPerLiterWith(decimal? marginUahPerLiter) =>
        FuelPricing.FinalPerLiterOrNull(
            SupplierPricePerLiter, marginUahPerLiter, PumpPricePerLiter, MinDiscountPerLiter);

    /// <summary>
/// What the per-litre pricing last changed, for the price-change audit trail.</summary>
    public DateTime? PriceUpdatedAt { get; set; }
    public Guid? PriceUpdatedByUserId { get; set; }

    // The price columns above are the CURRENT price, and every read path uses them. They are stamped by
    // OfferPriceRecorder, which also appends the row below - a convention would have been forgotten by
    // the sixth write path, and the failure is invisible: the price is right today and the history has
    // a hole in it. Two of the five paths did not stamp these two columns at all before that.

    /// <summary>
    /// Every price this offer has been priced at, newest last. Append-only, written by
    /// <c>OfferPriceRecorder</c> and never edited - see <see cref="FuelOfferPrice"/> for why the offer's
    /// own columns above are the current price and this is the record of how it got there.
    /// </summary>
    public ICollection<FuelOfferPrice> Prices { get; set; } = new List<FuelOfferPrice>();

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Archive, not delete. Same policy as <see cref="FuelPackage.IsDeleted"/> and for the same
    /// reason: an offer that has been sold against must stay readable for reports.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>When the offer was archived, or null while it is live.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>
    /// Which pool this offer belongs to: <see cref="DataKinds.Real"/> or <see cref="DataKinds.Qa"/>.
    /// Part of the natural key, not an attribute — the QA mirror of a live offer is a separate
    /// offer, and that is what lets the storefront answer for one pool without seeing the other.
    /// </summary>
    public string DataKind { get; set; } = DataKinds.Real;

    /// <summary>
    /// The sizes this offer sells in are not its own: they belong to the fuel, in
    /// <see cref="FuelNominal"/>. An offer is the pool and the price; the catalogue's shape is
    /// one answer for every pool, which is what keeps a rehearsal size from being a live one.
    /// </summary>

    /// <summary>
    /// Whether this offer is rehearsal data and must never reach the storefront. A view over
    /// <see cref="DataKind"/>, not a column — see <see cref="DataKinds"/>.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsTestData
    {
        get => DataKind == DataKinds.Qa;
        set => DataKind = value ? DataKinds.Qa : DataKinds.Real;
    }
}
