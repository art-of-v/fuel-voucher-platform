using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// <c>stations</c> was a brand network all along - okko, wog, klo - and six tables reached it through
/// a column called <c>station_id</c> that pointed at none of the filling stations.
/// <c>station_nodes</c> was the table that held those, one row per address.
/// <para>
/// The rename is the whole point: a column called <c>brand_id</c> beside a table called
/// <c>brand_stations</c> says what both are, once, and cannot be misread the way
/// <c>fuel_offers.station_id</c> could.
/// </para>
/// <para>
/// <b>Written as SQL rather than as EF operations, on purpose.</b> EF scaffolded this as a mix of
/// renames, constraint drops and re-adds, and its <c>Down</c> as a drop-and-recreate of both tables.
/// That pair is not a mirror: rolling back and re-applying - which seven of the integration tests do,
/// because a migration you cannot roll back is a deploy you cannot undo - failed on the second pass
/// with <c>relation "brand_stations" does not exist</c>. A forward migration that only renames, and a
/// backward one that only renames back, cannot drift apart: each statement here is the exact inverse
/// of the statement above it.
/// </para>
/// <para>
/// Nothing is dropped and nothing is created, so the rows in both tables and the values in all six
/// <c>station_id</c> columns are untouched - <c>okko</c> is still <c>okko</c>.
/// </para>
/// </summary>
public partial class StationsBecomeBrands : Migration
{
    /// <summary>
    /// The tables, then everything whose name embeds theirs. PostgreSQL keeps a constraint's name when
    /// the table it belongs to is renamed, so a rename without these leaves an error message pointing
    /// at a table that no longer exists.
    /// </summary>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        RenameBrands(migrationBuilder);
        RenameBrandStations(migrationBuilder);
        RenameBrandColumns(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        RenameBrandColumnsBack(migrationBuilder);
        RenameBrandStationsBack(migrationBuilder);
        RenameBrandsBack(migrationBuilder);
    }

    // --- the brand network ---------------------------------------------------------------

    private static void RenameBrands(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE "stations" RENAME TO "brands";""");
        migrationBuilder.Sql("""ALTER TABLE "brands" RENAME CONSTRAINT "PK_stations" TO "PK_brands";""");
        migrationBuilder.Sql("""ALTER TABLE "brands" RENAME CONSTRAINT "ck_stations_id_length" TO "ck_brands_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "brands" RENAME CONSTRAINT "ck_stations_name_length" TO "ck_brands_name_length";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_stations_name" RENAME TO "IX_brands_name";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_stations_station_type" RENAME TO "IX_brands_station_type";""");
    }

    private static void RenameBrandsBack(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER INDEX "IX_brands_station_type" RENAME TO "IX_stations_station_type";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_brands_name" RENAME TO "IX_stations_name";""");
        migrationBuilder.Sql("""ALTER TABLE "brands" RENAME CONSTRAINT "ck_brands_name_length" TO "ck_stations_name_length";""");
        migrationBuilder.Sql("""ALTER TABLE "brands" RENAME CONSTRAINT "ck_brands_id_length" TO "ck_stations_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "brands" RENAME CONSTRAINT "PK_brands" TO "PK_stations";""");
        migrationBuilder.Sql("""ALTER TABLE "brands" RENAME TO "stations";""");
    }

    // --- a filling station inside one ----------------------------------------------------

    private static void RenameBrandStations(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE "station_nodes" RENAME TO "brand_stations";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME CONSTRAINT "PK_station_nodes" TO "PK_brand_stations";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME CONSTRAINT "ck_station_nodes_id_length" TO "ck_brand_stations_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME CONSTRAINT "FK_station_nodes_stations_station_id" TO "FK_brand_stations_brands_brand_id";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_station_nodes_station_id" RENAME TO "IX_brand_stations_brand_id";""");
    }

    private static void RenameBrandStationsBack(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER INDEX "IX_brand_stations_brand_id" RENAME TO "IX_station_nodes_station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME CONSTRAINT "FK_brand_stations_brands_brand_id" TO "FK_station_nodes_stations_station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME CONSTRAINT "ck_brand_stations_id_length" TO "ck_station_nodes_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME CONSTRAINT "PK_brand_stations" TO "PK_station_nodes";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME TO "station_nodes";""");
    }

    // --- the six columns, and every index and CHECK whose name embeds them ----------------

    /// <summary>
    /// Each table's column, then its index and its length CHECK. A CHECK cannot simply be renamed:
    /// the definition itself names the column, so it is dropped and re-added in one statement, which
    /// is atomic - there is no window where the constraint does not exist.
    /// </summary>
    private static void RenameBrandColumns(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE "contracts" RENAME COLUMN "station_id" TO "brand_id";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_contracts_station_id" RENAME TO "IX_contracts_brand_id";""");
        migrationBuilder.Sql("""ALTER TABLE "contracts" DROP CONSTRAINT "ck_contracts_station_id_length", ADD CONSTRAINT "ck_contracts_brand_id_length" CHECK (length(brand_id) <= 100);""");
        migrationBuilder.Sql("""ALTER TABLE "contracts" RENAME CONSTRAINT "FK_contracts_stations_station_id" TO "FK_contracts_brands_brand_id";""");

        migrationBuilder.Sql("""ALTER TABLE "fuel_offers" RENAME COLUMN "station_id" TO "brand_id";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_fuel_offers_station_id" RENAME TO "IX_fuel_offers_brand_id";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_fuel_offers_station_id_fuel_type_id" RENAME TO "IX_fuel_offers_brand_id_fuel_type_id";""");
        migrationBuilder.Sql("""ALTER INDEX "ix_fuel_offers_station_fuel_pool" RENAME TO "ix_fuel_offers_brand_fuel_pool";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_offers" DROP CONSTRAINT "ck_fuel_offers_station_id_length", ADD CONSTRAINT "ck_fuel_offers_brand_id_length" CHECK (length(brand_id) <= 100);""");

        migrationBuilder.Sql("""ALTER TABLE "fuel_offer_prices" RENAME COLUMN "station_id" TO "brand_id";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_offer_prices" DROP CONSTRAINT "ck_fuel_offer_prices_station_id_length", ADD CONSTRAINT "ck_fuel_offer_prices_brand_id_length" CHECK (length(brand_id) <= 100);""");

        migrationBuilder.Sql("""ALTER TABLE "fuel_types" RENAME COLUMN "station_id" TO "brand_id";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_fuel_types_station_id" RENAME TO "IX_fuel_types_brand_id";""");
        migrationBuilder.Sql("""ALTER INDEX "ux_fuel_types_station_name" RENAME TO "ux_fuel_types_brand_name";""");
        migrationBuilder.Sql("""ALTER INDEX "ux_fuel_types_station_brand_fuel_code" RENAME TO "ux_fuel_types_brand_brand_fuel_code";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_types" DROP CONSTRAINT "ck_fuel_types_station_id_length", ADD CONSTRAINT "ck_fuel_types_brand_id_length" CHECK (length(brand_id) <= 100);""");

        migrationBuilder.Sql("""ALTER TABLE "fuel_type_product_codes" RENAME COLUMN "station_id" TO "brand_id";""");
        migrationBuilder.Sql("""ALTER INDEX "ux_fuel_type_product_codes_station_code" RENAME TO "ux_fuel_type_product_codes_brand_code";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_type_product_codes" DROP CONSTRAINT "ck_fuel_type_product_codes_station_id_length", ADD CONSTRAINT "ck_fuel_type_product_codes_brand_id_length" CHECK (length(brand_id) <= 100);""");

        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME COLUMN "station_id" TO "brand_id";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" DROP CONSTRAINT "ck_station_nodes_station_id_length", ADD CONSTRAINT "ck_brand_stations_brand_id_length" CHECK (length(brand_id) <= 100);""");
    }

    /// <summary>The exact inverse, in the exact reverse order, so a roll-back cycle is a no-op twice over.</summary>
    private static void RenameBrandColumnsBack(MigrationBuilder migrationBuilder)
    {
        // PostgreSQL refuses to rename a column a CHECK references ("other objects depend on it"),
        // so each table drops its length CHECK, renames, and re-adds it under the old name pointing at
        // the old column. The gap is inside the migration's transaction, so no session ever sees a
        // table without the constraint.
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" DROP CONSTRAINT "ck_brand_stations_brand_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" RENAME COLUMN "brand_id" TO "station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "brand_stations" ADD CONSTRAINT "ck_station_nodes_station_id_length" CHECK (length(station_id) <= 100);""");

        migrationBuilder.Sql("""ALTER TABLE "fuel_type_product_codes" DROP CONSTRAINT "ck_fuel_type_product_codes_brand_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_type_product_codes" RENAME COLUMN "brand_id" TO "station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_type_product_codes" ADD CONSTRAINT "ck_fuel_type_product_codes_station_id_length" CHECK (length(station_id) <= 100);""");
        migrationBuilder.Sql("""ALTER INDEX "ux_fuel_type_product_codes_brand_code" RENAME TO "ux_fuel_type_product_codes_station_code";""");

        migrationBuilder.Sql("""ALTER TABLE "fuel_types" DROP CONSTRAINT "ck_fuel_types_brand_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_types" RENAME COLUMN "brand_id" TO "station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_types" ADD CONSTRAINT "ck_fuel_types_station_id_length" CHECK (length(station_id) <= 100);""");
        migrationBuilder.Sql("""ALTER INDEX "IX_fuel_types_brand_id" RENAME TO "IX_fuel_types_station_id";""");
        migrationBuilder.Sql("""ALTER INDEX "ux_fuel_types_brand_name" RENAME TO "ux_fuel_types_station_name";""");
        migrationBuilder.Sql("""ALTER INDEX "ux_fuel_types_brand_brand_fuel_code" RENAME TO "ux_fuel_types_station_brand_fuel_code";""");

        migrationBuilder.Sql("""ALTER TABLE "fuel_offer_prices" DROP CONSTRAINT "ck_fuel_offer_prices_brand_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_offer_prices" RENAME COLUMN "brand_id" TO "station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_offer_prices" ADD CONSTRAINT "ck_fuel_offer_prices_station_id_length" CHECK (length(station_id) <= 100);""");

        migrationBuilder.Sql("""ALTER TABLE "fuel_offers" DROP CONSTRAINT "ck_fuel_offers_brand_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_offers" RENAME COLUMN "brand_id" TO "station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "fuel_offers" ADD CONSTRAINT "ck_fuel_offers_station_id_length" CHECK (length(station_id) <= 100);""");
        migrationBuilder.Sql("""ALTER INDEX "IX_fuel_offers_brand_id" RENAME TO "IX_fuel_offers_station_id";""");
        migrationBuilder.Sql("""ALTER INDEX "IX_fuel_offers_brand_id_fuel_type_id" RENAME TO "IX_fuel_offers_station_id_fuel_type_id";""");
        migrationBuilder.Sql("""ALTER INDEX "ix_fuel_offers_brand_fuel_pool" RENAME TO "ix_fuel_offers_station_fuel_pool";""");

        migrationBuilder.Sql("""ALTER TABLE "contracts" DROP CONSTRAINT "ck_contracts_brand_id_length";""");
        migrationBuilder.Sql("""ALTER TABLE "contracts" RENAME COLUMN "brand_id" TO "station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "contracts" ADD CONSTRAINT "ck_contracts_station_id_length" CHECK (length(station_id) <= 100);""");
        migrationBuilder.Sql("""ALTER INDEX "IX_contracts_brand_id" RENAME TO "IX_contracts_station_id";""");
        migrationBuilder.Sql("""ALTER TABLE "contracts" RENAME CONSTRAINT "FK_contracts_brands_brand_id" TO "FK_contracts_stations_station_id";""");
    }
}