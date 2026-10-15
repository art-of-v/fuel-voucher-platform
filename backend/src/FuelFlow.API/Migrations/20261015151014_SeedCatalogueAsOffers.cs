using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FuelFlow.Persistence;
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
    /// </remarks>
    public partial class SeedCatalogueAsOffers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var (offers, volumes) = SeedData.Catalogue;

            foreach (var offer in offers)
            {
                migrationBuilder.Sql($"""
                    INSERT INTO fuel_offers
                        (id, station_id, fuel_type_id, data_kind, supplier_price_per_liter,
                         margin_uah_per_liter, margin_percent, final_price_per_liter,
                         pump_price_per_liter, min_discount_per_liter, is_deleted, deleted_at_utc,
                         price_updated_at, price_updated_by_user_id, created_at_utc, updated_at_utc)
                    VALUES
                        ({Quote(offer.Id)}, {Quote(offer.StationId)}, {Quote(offer.FuelTypeId)}, {Quote(offer.DataKind)},
                         {Num(offer.SupplierPricePerLiter)}, {Num(offer.MarginUahPerLiter)}, {Num(offer.MarginPercent)},
                         {Num(offer.FinalPricePerLiter)}, NULL, NULL, FALSE, NULL, NULL, NULL,
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
                        ({Quote(volume.Id)}, {Quote(volume.OfferId)}, {Num(volume.Liters)}, {Num(volume.PackagePrice)},
                         FALSE, NULL, {Ts(volume.CreatedAtUtc)}, {Ts(volume.UpdatedAtUtc)})
                    ON CONFLICT (id) DO NOTHING;
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the seed rows are removed, and only ones this migration could have created. Rows
            // belonging to a real catalogue are not identified by anything in this migration, so the
            // Down does not guess: it removes what its own Up inserts, by key.
            var (offers, volumes) = SeedData.Catalogue;

            foreach (var volume in volumes.Reverse())
                migrationBuilder.Sql($"DELETE FROM fuel_offer_volumes WHERE id = {Quote(volume.Id)};");

            foreach (var offer in offers.Reverse())
                migrationBuilder.Sql($"DELETE FROM fuel_offers WHERE id = {Quote(offer.Id)};");
        }

        private static string Quote(string? value)
            => value is null ? "NULL" : $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

        private static string Num(decimal? value)
            => value is null ? "NULL" : value.Value.ToString(CultureInfo.InvariantCulture);

        private static string Ts(DateTime value)
            => $"'{value.ToUniversalTime():yyyy-MM-dd HH:mm:ss.ffffff}'";
    }
}