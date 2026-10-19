context.FuelTypes.Add(new FuelTypeEntity
    {
        Id = fuelTypeId,
        Name = $"AT {suffix}",
        BrandId = Brand,
        PumpPricePerLiter = 50m,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    });
    {
        Id = Guid.CreateVersion7(),
        BrandId = "at-x",
        FuelTypeId = fuelTypeId,
        DataKind = DataKinds.Real,
        PumpPricePerLiter = pump,
        FinalPricePerLiter = pump,
        EffectiveFromUtc = from,
        RecordedAtUtc = DateTime.UtcNow,
        Reason = reason,
    };
