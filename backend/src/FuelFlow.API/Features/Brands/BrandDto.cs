namespace FuelFlow.Features.Brands;

public sealed record BrandDto
{
    /// <summary>Surrogate key. Server-assigned; ignored on create.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// The network's own code, <c>"okko"</c> — what an operator types and what an import file
    /// carries. Unique. This is the identity a human means; <see cref="Id"/> is only what the
    /// database joins on.
    /// </summary>
    public string Slug { get; init; } = null!;

    public string Name { get; init; } = null!;
    public string LogoText { get; init; } = null!;
    public string Color { get; init; } = null!;

    /// <summary>Display priority in the mobile app; lower = higher. 999 = end of the list.</summary>
    public int SortOrder { get; init; } = 999;

    public List<BrandFuelDto> Fuels { get; init; } = [];
    public List<int> Nominals { get; init; } = [];
}

public sealed record BrandFuelDto
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;

    /// <summary>
    /// Cost per liter (собівартість) — what we pay. Manual entry in MVP.
    /// </summary>
    /// <remarks>
    /// <b>Nullable, and it has to stay nullable.</b> It was a plain <c>decimal</c>, which made "the operator
    /// left it blank" indistinguishable from "zero" — and the wire turns a JSON null into a zero before the
    /// value ever reaches this property. A fuel with no recorded cost therefore became a fuel with a cost of
    /// zero, and two things followed from that, both of them money:
    /// <list type="bullet">
    /// <item>the customer price became <c>min(0 + margin, column − discount)</c> — the MARGIN, not the price.
    /// A grade worth 92.90 became a 2.75 bargain, or 0.00 when the margin was blank too;</item>
    /// <item>the below-cost guard went blind, because it only fires when the cost is greater than zero — it
    /// reads "we paid nothing" as "nothing can be below what we paid".</item>
    /// </list>
    /// A null here means "not stated", and it stays that way: the price is then the column (what the driver
    /// pays) and the fuel is not sold until someone says what we paid.
    /// </remarks>
    public decimal? SupplierPricePerLiter { get; init; }

    /// <summary>
    /// Profit per liter (заробіток / МАРЖА knob). Null means "not stated", on the same terms as the cost
    /// above: a missing margin must never become a zero that publishes a free fuel.
    /// </summary>
    public decimal? MarginUahPerLiter { get; init; }

    public decimal? MarginPercent { get; init; }

    /// <summary>Computed customer price/liter = cost + profit.
    /// Server-authoritative; any value sent on write is ignored and recomputed. The column price is a
    /// reference, not a term of it, so this can sit above what the pump charges. Null when the fuel's cost or
    /// margin is not stated — there is no honest price to publish then.</summary>
    public decimal? FinalPricePerLiter { get; init; }

    /// <summary>Pump/board price (колонка), UAH/liter - the reference the customer's saving is
    /// measured against. Null until an operator records it; while null the price is the cost + profit.
    /// Manual in MVP.</summary>
    public decimal? PumpPricePerLiter { get; init; }

    /// <summary>Actual customer discount vs the column price (UAH/liter), derived for display:
    /// max(0, pump − the price we charge). Read-only output; not an input knob — the price is
    /// computed from the cost and the margin, and the column price is the reference it is measured
    /// against.</summary>
    public decimal DiscountPerLiter { get; init; }

    /// <summary>Manager standing opt-in: allow this supplier+fuel to be priced/sold BELOW blended
    /// cost (deliberate loss-leader). When false (default) the slice-3 hard block refuses below-cost
    /// pricing and sales.</summary>
    public bool AllowBelowCost { get; init; }

    public List<int> PackageLiters { get; init; } = [];

    /// <summary>
    /// Rehearsal flag for this fuel's packages, propagated to every package on save. Set it when
    /// adding or editing a fuel that exists only to rehearse a flow - a QA grade, a dry run against
    /// a real Brand. Flagged packages are never served to the storefront, so they cannot be bought,
    /// but they stay in the admin.
    /// </summary>
    /// <remarks>
    /// <b>Nullable and required, deliberately.</b> It used to be a plain <c>bool</c>, which made
    /// "the caller did not say" indistinguishable from "false" - and false means sell this. Since
    /// this field is written to <em>every</em> package of the fuel on every save, a client that
    /// omitted it while editing a QA fuel's margin published that fuel, with nothing saying so.
    /// A null now means the caller did not answer, and is a 400.
    /// </remarks>
    public bool? IsTestData { get; init; }

    /// <summary>
    /// Set only when this fuel's packages disagree on cost or on the customer price. The write path
    /// now refuses to create a fuel whose packages disagree on cost, and an edit from this screen
    /// rewrites every package to the values shown — so a non-null spread means the screen is about to
    /// flatten real per-nominal data, and the operator should know that before saving rather than
    /// after. Null in the ordinary case, where every package agrees.
    /// </summary>
    public BrandFuelPriceSpreadDto? PriceSpread { get; init; }

    /// <summary>
    /// The fuel's own code at the network ("A-95", "DP", "Pulls 95"). Read back so the screen can
    /// show it; sent to set or clear it. Null on read means the fuel has none.
    /// </summary>
    /// <remarks>
    /// Omitted on an edit leaves it alone; an empty string clears it. That asymmetry is deliberate —
    /// an edit that repriced a fuel must not silently drop the code a catalogue import matched on.
    /// </remarks>
    public string? BrandFuelCode { get; init; }
}

/// <summary>What the packages of one fuel actually disagree about, when they disagree.</summary>
public sealed record BrandFuelPriceSpreadDto
{
    public decimal MinSupplierPricePerLiter { get; init; }
    public decimal MaxSupplierPricePerLiter { get; init; }
    public decimal MinFinalPricePerLiter { get; init; }
    public decimal MaxFinalPricePerLiter { get; init; }
}

public sealed record CreateFuelRequest
{
    public string Name { get; init; } = null!;
    public decimal SupplierPricePerLiter { get; init; }
    public decimal MarginUahPerLiter { get; init; }
    public decimal? MarginPercent { get; init; }
    public decimal FinalPricePerLiter { get; init; }
    public decimal? PumpPricePerLiter { get; init; }
    public decimal DiscountPerLiter { get; init; }

    /// <summary>Manager standing opt-in to allow below-cost pricing/sale for this supplier+fuel.</summary>
    public bool AllowBelowCost { get; init; }

    public List<int> PackageLiters { get; init; } = [];

    /// <summary>
    /// The fuel's own code at the network ("A-95", "DP", "Pulls 95"). Optional: a fuel can exist
    /// without one, and two hand-entered fuels at one Brand must not collide over a value neither
    /// has. Unique among the live fuels of the same network that do have one.
    /// </summary>
    public string? BrandFuelCode { get; init; }

    /// <summary>
    /// Rehearsal flag, propagated to every package created for this fuel. Read back on
    /// <see cref="BrandFuelDto.IsTestData"/> so the screen can show what it set.
    /// </summary>
    /// <remarks>
    /// Nullable for the same reason as <see cref="BrandFuelDto.IsTestData"/>: a plain <c>bool</c>
    /// turned an omitted field into "publish this", which is the wrong default for the one field
    /// that decides whether rehearsal stock is sellable.
    /// </remarks>
    public bool? IsTestData { get; init; }
}

public sealed record AuditEventDto
{
    public Guid Id { get; init; }
    public string AggregateType { get; init; } = null!;
    public string AggregateId { get; init; } = null!;
    public string EventType { get; init; } = null!;
    public string? OldValue { get; init; }
    public string NewValue { get; init; } = null!;
    public string? ChangedByUserName { get; init; }
    public string Summary { get; init; } = null!;
    public DateTime ChangedAtUtc { get; init; }
}
