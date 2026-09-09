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
    public decimal SupplierPricePerLiter { get; init; }
    public decimal MarginUahPerLiter { get; init; }
    public decimal? MarginPercent { get; init; }
    public decimal FinalPricePerLiter { get; init; }

    /// <summary>
    /// Marketing discount (UAH/liter) shown against the pump price on the
    /// customer's card: fuel_types.BasePrice = Final + Discount,
    /// fuel_types.DiscountPrice = Final. The customer's chargeable price
    /// (fuel_packages.price) stays Final * liters - the discount is display,
    /// not billing.
    /// </summary>
    public decimal DiscountPerLiter { get; init; }

    public List<int> PackageLiters { get; init; } = [];
}

public sealed record CreateFuelRequest
{
    public string Name { get; init; } = null!;
    public decimal SupplierPricePerLiter { get; init; }
    public decimal MarginUahPerLiter { get; init; }
    public decimal? MarginPercent { get; init; }
    public decimal FinalPricePerLiter { get; init; }
    public decimal DiscountPerLiter { get; init; }
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
