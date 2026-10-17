using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Persistence;

internal static class SeedData
{
    // StationNode intentionally has no seed rows. The four demo АЗК that used to live here were
    // fabricated pins (UPG alone is a 498-pump network), so every fresh database dropped a phantom
    // marker onto the map. Real networks load through the admin import, which upserts by id -
    // see scripts/fetch-upg-stations.mjs for the UPG source.

    internal static readonly DateTime CreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static Station[] Stations =>
    [
        new() { Id = "okko", SortOrder = 1, Name = "OKKO", LogoText = "OKKO", Color = "#22c55e", Lat = 50.4851, Lng = 30.4734, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog", SortOrder = 2, Name = "WOG", LogoText = "WOG", Color = "#10b981", Lat = 50.4501, Lng = 30.5234, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "upg", SortOrder = 3, Name = "UPG", LogoText = "UPG", Color = "#06b6d4", Lat = 50.4001, Lng = 30.6134, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "klo", SortOrder = 4, Name = "KLO", LogoText = "KLO", Color = "#eab308", Lat = 50.4101, Lng = 30.4034, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc }
    ];

        // Only the OKKO fuels carry a brand fuel code, and that is a fact about evidence rather than a
    // preference. OKKO publishes a machine-readable list (`scripts/fetch-okko-prices.mjs`,
    // `/api/uk/fuels`) whose `fuel_code` values are the five below, so each one was read off that list
    // and checked against the fuel it names. WOG gets none because nothing has ever shown us WOG's
    // vocabulary: inventing "A-95" for WOG would be a guess about a supplier's naming that no list
    // supports, and a guess here is an identity the unique index then enforces.
    //
    // With the codes, a freshly migrated catalogue is born keyed — so the catalogue import recognises
    // these five instead of creating a second "А-95" beside "A-95" — and the seed script resolves by
    // name against real rows in every environment.
    internal static FuelTypeEntity[] FuelTypes =>
    [
        new() { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BrandFuelCode = "DP", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "okko-95", Name = "A-95", StationId = "okko", BrandFuelCode = "A-95", BasePrice = 54, DiscountPrice = 51, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BrandFuelCode = "Pulls 95", BasePrice = 60, DiscountPrice = 56, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "okko-pulls-dp", Name = "ДП PULLS", StationId = "okko", BrandFuelCode = "Pulls Diesel", BasePrice = 58, DiscountPrice = 55, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "okko-gas", Name = "ГАЗ", StationId = "okko", BrandFuelCode = "SPBT", BasePrice = 29, DiscountPrice = 27, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-dp", Name = "ДП Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-95-euro", Name = "A 95 EURO", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-100", Name = "Mustang 100", StationId = "wog", BasePrice = 65, DiscountPrice = 61, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = "wog-gas", Name = "ГАЗ", StationId = "wog", BasePrice = 29, DiscountPrice = 27, CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc }
    ];

    /// <summary>
    /// Seeded as an offer per fuel plus its volumes, because that is what the application reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old shape seeded one row per package, which meant the per-litre figures were written N
    /// identical times per fuel and the seed had to restate them on every row. Here they are written
    /// once on the offer.
    /// </para>
    /// <para>
    /// <b>Volume ids are the old package ids</b> - <c>{fuelTypeId}-{liters}</c> - deliberately. A fresh
    /// database gets the same package identifiers it always had, so seeded fixtures, QA scripts and
    /// anything that hard-codes a package id keep resolving after this change.
    /// </para>
    /// <para>
    /// The live pool only. A rehearsal catalogue is seeded explicitly by the QA seed scripts, and
    /// seeding both here would put QA rows into every fresh developer database.
    /// </para>
    /// </remarks>
    internal static (FuelOffer[] Offers, FuelOfferVolume[] Volumes) Catalogue
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

            var offers = new List<FuelOffer>();
            var volumes = new List<FuelOfferVolume>();

            foreach (var ft in FuelTypes)
            {
                var margin = fuelPricing.TryGetValue(ft.Id, out var p) ? p.margin : 2.0m;
                var finalPrice = ft.DiscountPrice;
                var supplierPrice = finalPrice - margin;

                var offer = new FuelOffer
                {
                    Id = OfferKeys.For(ft.Id, DataKinds.Real),
                    StationId = ft.StationId,
                    FuelTypeId = ft.Id,
                    DataKind = DataKinds.Real,
                    SupplierPricePerLiter = supplierPrice,
                    MarginUahPerLiter = margin,
                    // No `final_price_per_liter`: the offer stopped storing the derived figure on
                    // 2026-10-17, and the seed needs no override - `supplier + margin` is exactly
                    // `finalPrice`, since no pump price is seeded and the ceiling does not bind.
                    // No pump price is seeded, so the public "before" price is just the sale price:
                    // the store-front shows no fabricated saving and never the supplier cost (planning #73).
                    CreatedAtUtc = CreatedAtUtc,
                    UpdatedAtUtc = CreatedAtUtc,
                };
                offers.Add(offer);

                foreach (var liters in new[] { 10m, 20m, 50m })
                {
                    volumes.Add(new FuelOfferVolume
                    {
                        Id = $"{ft.Id}-{liters}",
                        OfferId = offer.Id,
                        Liters = liters,
                        PackagePrice = Math.Round(finalPrice * liters, 2, MidpointRounding.AwayFromZero),
                        CreatedAtUtc = CreatedAtUtc,
                        UpdatedAtUtc = CreatedAtUtc,
                    });
                }
            }

            // The two extra small nominals for ДП ЄВРО, carried over verbatim from the old seed.
            var dp = offers.First(o => o.FuelTypeId == "okko-dp");
            volumes.Add(new FuelOfferVolume
            {
                Id = "okko-dp-2",
                OfferId = dp.Id,
                Liters = 2m,
                PackagePrice = 104,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc,
            });
            volumes.Add(new FuelOfferVolume
            {
                Id = "okko-dp-3",
                OfferId = dp.Id,
                Liters = 3m,
                PackagePrice = 156,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc,
            });

            return (offers.ToArray(), volumes.ToArray());
        }
    }
}
