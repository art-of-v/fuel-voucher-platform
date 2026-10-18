using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Seeds the catalogue as offers plus volumes, because that is what the application reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is hand-written.</b> EF scaffolds this change as 74 <c>DeleteData</c> statements
    /// against <c>fuel_packages</c> followed by inserts into the new tables, because the seed moved. On a
    /// database that was already migrated by <c>SplitFuelPackagesIntoOffers</c> - which is every
    /// environment that matters, production included - those rows are real catalogue, not seed, and
    /// deleting them destroys the rollback this very release depends on. So nothing is deleted here, and
    /// the inserts are written to skip what is already there.
    /// </para>
    /// <para>
    /// <b>Why it reads <c>SeedData.Catalogue</c> rather than repeating the rows.</b> One source of truth.
    /// A second copy of the seed in a migration is a second thing to forget to update, and the failure
    /// mode is a fresh developer database that disagrees with production about what the catalogue is.
    /// The usual objection - that migrations should not depend on app code - is real but weakest here:
    /// this migration's entire purpose is to install the seed, so the seed is the thing it must agree
    /// with.
    /// </para>
/// <para>
    /// <b>Idempotence is the point, not a nicety.</b> Production already carries these offers from
    /// <c>SplitFuelPackagesIntoOffers</c>, at the same keys. A plain INSERT would fail there on the
    /// unique constraint and take the deploy - and the API - down.
    /// </para>
/// <para>
/// <b>Which key it writes, and why it is not the current one.</b> The offers are keyed the way
    /// <c>SplitFuelPackagesIntoOffers</c> keyed them - <c>station_id || '-' || fuel_type_id</c> - rather
    /// than through <c>OfferKeys</c>, which on 2026-10-17 stopped prefixing the station. This migration
    /// runs BEFORE <c>DropTheStationPrefixFromOfferKeys</c>, and on a fresh database the offers it is
    /// about to insert already exist: the backfill created them from the seeded <c>fuel_packages</c>
/// rows. Writing today's key would collide on <c>ix_fuel_offers_station_fuel_pool</c> instead of on
    /// <c>id</c>, and <c>ON CONFLICT (id) DO NOTHING</c> only covers the latter - so a fresh database
    /// would fail to build, and a second row per fuel would be the alternative. Two migrations stating
    /// one rule in two shapes is the price of append-only history; the third re-keys both to the current
    /// one, and this one is not edited again.
    /// </para>
/// </remarks>
    /// <para>
    /// <b>Edited 2026-10-17, once, and only to keep compiling.</b> <c>fuel_offers.final_price_per_liter</c>
    /// was dropped by <c>DeriveTheOfferFinalPrice</c>, after this migration had already run everywhere. The
    /// column still exists at this point in history, so the statement below is unchanged in what it does -
    /// it still writes the value the seed's own arithmetic produces - but it reads the figure from
    /// <c>ComputedFinalPricePerLiter</c> because the stored property no longer exists. Rewriting history is
    /// not on the table; a migration that cannot compile is a migration nobody can roll back.
    /// </para>
    /// <para>
    /// <b>Edited 2026-10-18, again only to keep compiling, and the statement is still unchanged.</b>
    /// The margin moved to <c>fuel_types</c>, so the no-argument <c>ComputedFinalPricePerLiter</c> went
    /// with it: it read a column that nothing writes now, and on this seed - which predates the move -
    /// that column is still correct, which is why it is passed in explicitly rather than deleted. The
    /// value emitted below is byte-identical to the 2026-10-17 build, and this migration has already
    /// run everywhere, so there is no database it could run against anyway.
    /// </para>
    public partial class SeedCatalogueAsOffers : Migration
    {
        /// <summary>
        /// The <c>station_id</c> this migration's offer rows carried: the seeded network's own code.
        /// </summary>
        /// <remarks>
        /// Added 2026-10-18 for the third time, and for the same reason as the two edits above: planning
        /// D-21 gave <c>brands</c> a uuid id, so <c>FuelOffer.BrandId</c> is a <c>Guid</c> and can no
        /// longer be quoted as the string the <c>station_id</c> column held. That column is still a
        /// <c>text</c> natural key at this point in history and still wants the network's CODE.
        /// Formatting the id instead would write a uuid into a natural-key column — a different
        /// statement, and a key the next migration in the chain does not expect. The mapping comes from
        /// <see cref="SeedData.Brands"/> because the seed is already the one source of truth this file
        /// trusts.
        /// </remarks>
        private static string StationId(FuelOffer offer)
            => SeedData.Brands.First(b => b.Id == offer.BrandId).Slug;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (offers, volumes) = SeedData.Catalogue;

            // The key as it stood when this migration shipped: the station, then the fuel, then the pool
            // suffix. OfferKeys moved on 2026-10-17 and this file deliberately did not - see the class
            // remark. Keyed through the offer's own columns rather than through offer.Id, so the shape
            // is stated here where the migration lives instead of being read out of a seed object whose
            // keys have since changed.
            var historicalKey = offers.ToDictionary(
                o => o.Id,
                o => $"{StationId(o)}-{o.FuelTypeId}" + (o.DataKind == DataKinds.Qa ? "-qa" : ""));

            foreach (var offer in offers)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO fuel_offers
                        (id, station_id, fuel_type_id, data_kind, supplier_price_per_liter,
                         margin_uah_per_liter, margin_percent, final_price_per_liter,
                         pump_price_per_liter, min_discount_per_liter, is_deleted, deleted_at_utc,
                         price_updated_at, price_updated_by_user_id, created_at_utc, updated_at_utc)
                    VALUES
                        ({Quote(historicalKey[offer.Id])}, {Quote(StationId(offer))}, {Quote(offer.FuelTypeId)}, {Quote(offer.DataKind)},
                         {Num(offer.SupplierPricePerLiter)}, {Num(offer.FinalPerLiterWith(0m) - 0m)}, {Num(offer.MarginPercent)},
                         {Num(offer.FinalPerLiterWith(offer.FinalPerLiterWith(0m) - 0m))}, NULL, NULL, FALSE, NULL, NULL, NULL,
                         {Ts(offer.CreatedAtUtc)}, {Ts(offer.UpdatedAtUtc)})
                    ON CONFLICT (id) DO NOTHING;
                    """);
            }

            foreach (var volume in volumes)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO fuel_offer_volumes
                        (id, offer_id, liters, package_price, is_deleted, deleted_at_utc,
                         created_at_utc, updated_at_utc)
                    VALUES
                        ({Quote(volume.Id)}, {Quote(historicalKey[volume.OfferId])}, {Num(volume.Liters)}, {Num(SeededPackagePrice(volume))},
                         FALSE, NULL, {Ts(volume.CreatedAtUtc)}, {Ts(volume.UpdatedAtUtc)})
                    ON CONFLICT (id) DO NOTHING;
                    """);
            }
        }

        /// <summary>
        /// The price this migration's volume rows carry, which the current entity no longer has.
        /// </summary>
        /// <remarks>
        /// Second hand-edited line in this file, same reason as the offer's final price above:
        /// <c>package_price</c> was dropped by <c>PackagePriceOverrideOnNominals</c> after this migration
        /// had already run everywhere, and the seed rows it writes here have to keep the totals they wrote
        /// then. Recomputed from the seed's own numbers — the offer's rate × the volume's litres — which is
        /// exactly what those rows held: <c>finalPrice × liters</c>, with no pump price seeded so the rate
        /// is <c>cost + margin</c>.
        /// </remarks>
        private static decimal SeededPackagePrice(FuelOfferVolume volume)
        {
            var offer = SeedData.Catalogue.Offers.First(o => o.Id == volume.OfferId);
            var perLiter = FuelPricing.FinalPerLiterOrNull(
                offer.SupplierPricePerLiter,
                offer.FinalPerLiterWith(0m) - 0m,
                offer.PumpPricePerLiter,
                offer.MinDiscountPerLiter);

            if (perLiter is not { } price)
            {
                throw new InvalidOperationException(
                    $"Seeded volume {volume.Id} has no derivable price, so this migration cannot reproduce the "
                    + "package_price it wrote originally.");
            }

            return Math.Round(price * volume.Liters, 2, MidpointRounding.AwayFromZero);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the seed rows are removed, and only ones this migration could have created. Rows
            // belonging to a real catalogue are not identified by anything in this migration, so the
            // Down does not guess: it removes what its own Up inserts, by key - the same keys the Up
            // inserts them under, which on a database this migration has already run are long gone.
            var (offers, volumes) = SeedData.Catalogue;

            var historicalKey = offers.ToDictionary(
                o => o.Id,
                o => $"{StationId(o)}-{o.FuelTypeId}" + (o.DataKind == DataKinds.Qa ? "-qa" : ""));

            foreach (var volume in volumes.Reverse())
                migrationBuilder.Sql($"DELETE FROM fuel_offer_volumes WHERE id = {Quote(volume.Id)};");

            foreach (var offer in offers.Reverse())
                migrationBuilder.Sql($"DELETE FROM fuel_offers WHERE id = {Quote(historicalKey[offer.Id])};");
        }

        private static string Quote(string? value)
            => value is null ? "NULL" : $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

        private static string Num(decimal? value)
            => value is null ? "NULL" : value.Value.ToString(CultureInfo.InvariantCulture);

        private static string Ts(DateTime value)
            => $"'{value.ToUniversalTime():yyyy-MM-dd HH:mm:ss.ffffff}'";
    }
}