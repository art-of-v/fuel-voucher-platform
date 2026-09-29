namespace FuelFlow.Features.Providers;

public sealed record ProviderDto
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string LogoText { get; init; } = null!;
    public string Color { get; init; } = null!;

    /// <summary>Display priority in the mobile app; lower = higher. 999 = end of the list.</summary>
    public int SortOrder { get; init; } = 999;

    public List<ProviderFuelDto> Fuels { get; init; } = [];
    public List<int> Nominals { get; init; } = [];
}

public sealed record ProviderFuelDto
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;

    /// <summary>Cost per liter (собівартість). Manual entry in MVP.</summary>
    public decimal SupplierPricePerLiter { get; init; }

    /// <summary>Profit per liter (заробіток / МАРЖА knob).</summary>
    public decimal MarginUahPerLiter { get; init; }

    public decimal? MarginPercent { get; init; }

    /// <summary>Computed customer price/liter = min(cost + profit, pump − minDiscount).
    /// Server-authoritative; any value sent on write is ignored and recomputed.</summary>
    public decimal FinalPricePerLiter { get; init; }

    /// <summary>Pump/board price (колонка), UAH/liter - the ceiling. Null until an
    /// operator records it; while null the price is the legacy cost + profit. Manual in MVP.</summary>
    public decimal? PumpPricePerLiter { get; init; }

    /// <summary>Minimum guaranteed discount under the pump price (UAH/liter): the floor
    /// of pump − final. Operator knob; default ≈ 0.50. Stored per package.</summary>
    public decimal MinDiscountPerLiter { get; init; }

    /// <summary>Actual customer discount vs pump (UAH/liter), derived for display:
    /// max(0, BasePrice − DiscountPrice). Read-only output; not an input knob.</summary>
    public decimal DiscountPerLiter { get; init; }

    /// <summary>Manager standing opt-in: allow this supplier+fuel to be priced/sold BELOW blended
    /// cost (deliberate loss-leader). When false (default) the slice-3 hard block refuses below-cost
    /// pricing and sales.</summary>
    public bool AllowBelowCost { get; init; }

    public List<int> PackageLiters { get; init; } = [];
}

public sealed record CreateFuelRequest
{
    public string Name { get; init; } = null!;
    public decimal SupplierPricePerLiter { get; init; }
    public decimal MarginUahPerLiter { get; init; }
    public decimal? MarginPercent { get; init; }
    public decimal FinalPricePerLiter { get; init; }
    public decimal? PumpPricePerLiter { get; init; }
    public decimal MinDiscountPerLiter { get; init; }
    public decimal DiscountPerLiter { get; init; }

    /// <summary>Manager standing opt-in to allow below-cost pricing/sale for this supplier+fuel.</summary>
    public bool AllowBelowCost { get; init; }

    public List<int> PackageLiters { get; init; } = [];
}

public sealed record ProviderEventDto
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
