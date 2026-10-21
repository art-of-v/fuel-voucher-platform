using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Persistence;

internal static class SeedData
{
    // BrandStation intentionally has no seed rows. The four demo АЗК that used to live here were
    // fabricated pins (UPG alone is a 498-pump network), so every fresh database dropped a phantom
    // marker onto the map. Real networks load through the admin import, which upserts by id -
    // see scripts/fetch-upg-stations.mjs for the UPG source.

    internal static readonly DateTime CreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static Brand[] Brands =>
    [
        new() { Id = BrandIds.Okko, Slug = "okko", SortOrder = 1, Name = "OKKO", LogoText = "OKKO", Color = "#22c55e", CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = BrandIds.Wog, Slug = "wog", SortOrder = 2, Name = "WOG", LogoText = "WOG", Color = "#10b981", CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = BrandIds.Upg, Slug = "upg", SortOrder = 3, Name = "UPG", LogoText = "UPG", Color = "#06b6d4", CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc },
        new() { Id = BrandIds.Klo, Slug = "klo", SortOrder = 4, Name = "KLO", LogoText = "KLO", Color = "#eab308", CreatedAtUtc = CreatedAtUtc, UpdatedAtUtc = CreatedAtUtc }
    ];

    /// <summary>
    /// One seeded fuel, with the natural key it was identified by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is kept because two readers still need it. The catalogue's own id is a uuid now, derived
    /// from this key by <see cref="FuelIdMapper"/>, and the conversion migration uses the same rule -
    /// so a row written before the conversion and a row written after it are the same row.
    /// </para>
    /// <para>
    /// The second reader is the migration history. <c>20261015151014_SeedCatalogueAsOffers</c> reads
    /// <see cref="Catalogue"/> and writes the fuel's key into a text column that, at that point in
    /// history, still holds keys. A uuid there would be a key the rows around it do not have.
    /// </para>
    /// </remarks>
    private sealed record SeedFuel(
        string Key,
        string Name,
        Guid BrandId,
        string? BrandFuelCode,
        decimal BasePrice,
        decimal DiscountPrice);

    // Only the OKKO fuels carry a brand fuel code, and that is a fact about evidence rather than a
    // preference. OKKO publishes a machine-readable list (`scripts/fetch-okko-prices.mjs`,
    // `/api/uk/fuels`) whose `fuel_code` values are the five below, so each one was read off that list
    // and checked against the fuel it names. WOG gets none because nothing has ever shown us WOG's
    // vocabulary: inventing "A-95" for WOG would be a guess about a supplier's naming that no list
    // supports, and a guess here is an identity the unique index then enforces.
    //
    // With the codes, a freshly migrated catalogue is born keyed — so the catalogue import recognises
    // these five instead of creating a second "А-95" beside "А-95" — and the seed script resolves by
    // name against real rows in every environment.
    private static readonly SeedFuel[] SeedFuels =
    [
        new("okko-dp", "ДП ЄВРО", BrandIds.Okko, "DP", 55, 52),
        new("okko-95", "A-95", BrandIds.Okko, "A-95", 54, 51),
        new("okko-p95", "Pulls 95", BrandIds.Okko, "Pulls 95", 60, 56),
        new("okko-pulls-dp", "ДП PULLS", BrandIds.Okko, "Pulls Diesel", 58, 55),
        new("okko-gas", "ГАЗ", BrandIds.Okko, "SPBT", 29, 27),
        new("wog-dp", "ДП Mustang", BrandIds.Wog, null, 56, 53),
        new("wog-95", "A-95 Mustang", BrandIds.Wog, null, 55, 52),
        new("wog-95-euro", "A 95 EURO", BrandIds.Wog, null, 56, 53),
        new("wog-100", "Mustang 100", BrandIds.Wog, null, 65, 61),
        new("wog-gas", "ГАЗ", BrandIds.Wog, null, 29, 27)
    ];

    /// <summary>
    /// The seeded fuels, with the uuid id each of them is stored under.
    /// </summary>
    /// <remarks>
    /// The id is derived, not written down: <see cref="FuelIdMapper"/> maps the natural key to a uuid
    /// the same way the conversion migration maps the rows that carried the key, so the seeded row and
    /// a converted row are one row and not two.
    /// </remarks>
    internal static FuelTypeEntity[] FuelTypes =>
        SeedFuels.Select(fuel => new FuelTypeEntity
        {
            Id = FuelIdMapper.ToUuid(fuel.Key),
            Name = fuel.Name,
            BrandId = fuel.BrandId,
            BrandFuelCode = fuel.BrandFuelCode,
            BasePrice = fuel.BasePrice,
            DiscountPrice = fuel.DiscountPrice,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc
        }).ToArray();

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
    /// <b>And the offer's fuel id is the natural key too</b>, for the same reason: this is what
    /// <c>20261015151014_SeedCatalogueAsOffers</c> writes into a text column, at a point in history
    /// where the fuels still carry their keys. Writing a uuid there would be a key nothing else in
    /// that schema has.
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

            foreach (var fuel in SeedFuels)
            {
                var margin = fuelPricing.TryGetValue(fuel.Key, out var p) ? p.margin : 2.0m;
                var finalPrice = fuel.DiscountPrice;
                var supplierPrice = finalPrice - margin;

                var offer = new FuelOffer
                {
                    Id = OfferKeys.For(fuel.Key, DataKinds.Real),
                    BrandId = fuel.BrandId,
                    FuelTypeId = fuel.Key,
                    DataKind = DataKinds.Real,
                    SupplierPricePerLiter = supplierPrice,
                    // No margin here: it is the fuel's, set below. The offer repeated it for a while,
                    // and two copies of one number is how they drift.
                    // No `final_price_per_liter`: the offer stopped storing the derived figure on
                    // 2026-10-17, and the seed needs no override - `supplier + margin` is exactly
                    // `finalPrice`, since the column price is a reference and not a term of it.
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
                        Id = $"{fuel.Key}-{liters}",
                        OfferId = offer.Id,
                        Liters = liters,
                        // No override: the seeded catalogue is priced from its fuel's rate, which is what
                        // the old stored total worked out to anyway (finalPrice = cost + margin, no pump).
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
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc,
            });
            volumes.Add(new FuelOfferVolume
            {
                Id = "okko-dp-3",
                OfferId = dp.Id,
                Liters = 3m,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = CreatedAtUtc,
            });

            return (offers.ToArray(), volumes.ToArray());
        }
    }
}
