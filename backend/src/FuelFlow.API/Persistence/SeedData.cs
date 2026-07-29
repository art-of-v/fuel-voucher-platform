using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Persistence;

internal static class SeedData
{
    internal static readonly DateTime CreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static Station[] Stations =>
    [
        new() { Id = "okko", Name = "OKKO", LogoText = "OKKO", Color = "#22c55e", Lat = 50.4851, Lng = 30.4734, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog", Name = "WOG", LogoText = "WOG", Color = "#10b981", Lat = 50.4501, Lng = 30.5234, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "upg", Name = "UPG", LogoText = "UPG", Color = "#06b6d4", Lat = 50.4001, Lng = 30.6134, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "klo", Name = "KLO", LogoText = "KLO", Color = "#eab308", Lat = 50.4101, Lng = 30.4034, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc }
    ];

    internal static StationNode[] StationNodes =>
    [
        new() { Id = "okko-kyiv-main", StationId = "okko", Name = "OKKO Київ", Address = "42 Чоколівський бульвар", City = "Київ", StationType = "Тип АЗС ОККО-міська", Lat = 50.4310, Lng = 30.4515, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-kyiv-main", StationId = "wog", Name = "WOG Київ", Address = "15-Б проспект Соборності", City = "Київ", StationType = "Тип АЗС WOG-міська", Lat = 50.4482, Lng = 30.6170, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "upg-kyiv-main", StationId = "upg", Name = "UPG Київ", Address = "Проспект Перемоги, 98", City = "Київ", StationType = "Тип АЗС UPG-міська", Lat = 50.4566, Lng = 30.3950, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "klo-kyiv-main", StationId = "klo", Name = "KLO Київ", Address = "Броварський проспект, 11", City = "Київ", StationType = "Тип АЗС KLO-міська", Lat = 50.4578, Lng = 30.5986, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc }
    ];

    internal static FuelTypeEntity[] FuelTypes =>
    [
        new() { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 54, DiscountPrice = 51, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BasePrice = 60, DiscountPrice = 56, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "okko-pulls-dp", Name = "ДП PULLS", StationId = "okko", BasePrice = 58, DiscountPrice = 55, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "okko-gas", Name = "ГАЗ", StationId = "okko", BasePrice = 29, DiscountPrice = 27, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-dp", Name = "ДП Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-95-euro", Name = "A 95 EURO", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-100", Name = "Mustang 100", StationId = "wog", BasePrice = 65, DiscountPrice = 61, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-gas", Name = "ГАЗ", StationId = "wog", BasePrice = 29, DiscountPrice = 27, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc }
    ];

    internal static FuelPackage[] FuelPackages
    {
        get
        {
            var fuelPricing = new Dictionary<string, (decimal margin, int sort)>
            {
                ["okko-dp"] = (2.0m, 1),
                ["okko-95"] = (2.0m, 2),
                ["okko-p95"] = (3.0m, 3),
                ["okko-pulls-dp"] = (2.5m, 4),
                ["okko-gas"] = (1.5m, 5),
                ["wog-dp"] = (2.0m, 6),
                ["wog-95"] = (2.0m, 7),
                ["wog-95-euro"] = (2.5m, 8),
                ["wog-100"] = (3.0m, 9),
                ["wog-gas"] = (1.5m, 10),
            };

            return FuelTypes
                .SelectMany(ft =>
                {
                    var margin = fuelPricing.TryGetValue(ft.Id, out var p) ? p.margin : 2.0m;
                    var finalPrice = (decimal)ft.DiscountPrice;
                    var supplierPrice = finalPrice - margin;
                    return new[] { 10m, 20m, 50m }.Select(liters => new FuelPackage
                    {
                        Id = $"{ft.Id}-{liters}",
                        StationId = ft.StationId,
                        FuelTypeId = ft.Id,
                        FuelName = ft.Name,
                        Liters = liters,
                        Price = (int)Math.Round(finalPrice * liters),
                        OriginalPrice = (int)Math.Round(supplierPrice * liters),
                        SupplierPricePerLiter = supplierPrice,
                        MarginUahPerLiter = margin,
                        FinalPricePerLiter = finalPrice,
                        CreatedAtUtc = CreatedAtUtc,
                        UpdatedAtUtc = CreatedAtUtc
                    });
                })
                .Append(new FuelPackage
                {
                    Id = "okko-dp-2",
                    StationId = "okko",
                    FuelTypeId = "okko-dp",
                    FuelName = "ДП ЄВРО",
                    Liters = 2m,
                    Price = 104,
                    OriginalPrice = 100,
                    SupplierPricePerLiter = 50m,
                    MarginUahPerLiter = 2m,
                    FinalPricePerLiter = 52m,
                    CreatedAtUtc = CreatedAtUtc,
                    UpdatedAtUtc = CreatedAtUtc
                })
                .Append(new FuelPackage
                {
                    Id = "okko-dp-3",
                    StationId = "okko",
                    FuelTypeId = "okko-dp",
                    FuelName = "ДП ЄВРО",
                    Liters = 3m,
                    Price = 156,
                    OriginalPrice = 150,
                    SupplierPricePerLiter = 50m,
                    MarginUahPerLiter = 2m,
                    FinalPricePerLiter = 52m,
                    CreatedAtUtc = CreatedAtUtc,
                    UpdatedAtUtc = CreatedAtUtc
                })
                .ToArray();
        }
    }
}
