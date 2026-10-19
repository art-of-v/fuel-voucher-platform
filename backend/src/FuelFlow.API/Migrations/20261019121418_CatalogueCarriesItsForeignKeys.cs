using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class CatalogueCarriesItsForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
// The one fuel the check below would refuse, fixed before the check lands.
            //
            // OKKO "Пульс 95" (brand_fuel_code 'Pulls 95', id 01a14b20-c2f0-7207-b803-43b246a8eb08)
            // carries discount_price 0.00 against base_price and pump_price_per_liter 95.90. Its
            // single offer row - catalogue-import, 2026-10-17 - holds final_price_per_liter NULL,
            // with the pump price present and no supplier cost and no margin. The 0 is not a price
            // anybody set; it is what OfferPricingBecomesTheFuel wrote when it copied that NULL
            // across, and the storefront would have sold the grade for nothing.
            //
            // Priced at the pump price: 95.90 is what every other OKKO grade with no supplier cost
            // and no margin carries, and it is the number the fuel already held two columns over.
            migrationBuilder.Sql("""
                UPDATE fuel_types
                   SET discount_price = pump_price_per_liter
                 WHERE discount_price <= 0
                   AND pump_price_per_liter > 0
                   AND discount_price IS DISTINCT FROM pump_price_per_liter;
                """);

            // Refuse whatever the UPDATE could not settle, rather than accept it. A zero selling
            // price next to a known pump price is the leak; one with no pump price is the giveaway
            // case the constraint deliberately permits, and this is here so the migration stops and
            // asks rather than quietly deciding a price. Verified against a copy of production on
            // 2026-10-19: the single offending row is repaired above and this finds nothing.
            migrationBuilder.Sql("""
                DO $$ DECLARE bad int; BEGIN
                    SELECT count(*) INTO bad FROM fuel_types
                        WHERE discount_price <= 0 AND pump_price_per_liter IS NOT NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'fuel_types: % row(s) sell at zero or less while carrying a pump price. Price them deliberately and re-run.', bad; END IF;
                END $$;
                """);

            // Refuse before the ALTERs, not after. Each of these counts rows that already point at
            // a parent that does not exist; every count is zero on production as measured on
            // 2026-10-19, and a non-zero one is the finding - it means a write path reached the
            // database without the constraint this migration adds.
            //
            // Same shape and the same reasoning as AddForeignKey in
            // 20260919113317_AddMissingDriftedTables: every statement is its own DO $EF$ block,
            // because EF cannot nest PL/pgSQL.
            migrationBuilder.Sql("""
                DO $$ DECLARE bad int; BEGIN
                    SELECT count(*) INTO bad FROM fuel_types c
                        LEFT JOIN brands b ON b.id = c.brand_id WHERE b.id IS NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'fuel_types.brand_id: % row(s) name a brand that does not exist', bad; END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$ DECLARE bad int; BEGIN
                    SELECT count(*) INTO bad FROM fuel_offer_prices c
                        LEFT JOIN brands b ON b.id = c.brand_id WHERE b.id IS NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'fuel_offer_prices.brand_id: % row(s) name a brand that does not exist', bad; END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$ DECLARE bad int; BEGIN
                    SELECT count(*) INTO bad FROM fuel_offer_prices c
                        LEFT JOIN fuel_types f ON f.id = c.fuel_type_id WHERE f.id IS NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'fuel_offer_prices.fuel_type_id: % price row(s) name a fuel that does not exist', bad; END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$ DECLARE bad int; BEGIN
                    SELECT count(*) INTO bad FROM fuel_type_product_codes c
                        LEFT JOIN brands b ON b.id = c.brand_id WHERE b.id IS NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'fuel_type_product_codes.brand_id: % row(s) name a brand that does not exist', bad; END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$ DECLARE bad int; BEGIN
                    SELECT count(*) INTO bad FROM fuel_type_product_codes c
                        LEFT JOIN fuel_types f ON f.id = c.fuel_type_id WHERE f.id IS NULL;
                    IF bad > 0 THEN RAISE EXCEPTION
                        'fuel_type_product_codes.fuel_type_id: % row(s) name a fuel that does not exist', bad; END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<decimal>(
                name: "margin_uah_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_zero_price_needs_no_pump_price",
                table: "fuel_types",
                sql: "discount_price > 0 OR pump_price_per_liter IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_offer_prices_brand_id",
                table: "fuel_offer_prices",
                column: "brand_id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_offer_prices_brands_brand_id",
                table: "fuel_offer_prices",
                column: "brand_id",
                principalTable: "brands",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_offer_prices_fuel_types_fuel_type_id",
                table: "fuel_offer_prices",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_type_product_codes_brands_brand_id",
                table: "fuel_type_product_codes",
                column: "brand_id",
                principalTable: "brands",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_type_product_codes_fuel_types_fuel_type_id",
                table: "fuel_type_product_codes",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_types_brands_brand_id",
                table: "fuel_types",
                column: "brand_id",
                principalTable: "brands",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_offer_prices_brands_brand_id",
                table: "fuel_offer_prices");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_offer_prices_fuel_types_fuel_type_id",
                table: "fuel_offer_prices");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_type_product_codes_brands_brand_id",
                table: "fuel_type_product_codes");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_type_product_codes_fuel_types_fuel_type_id",
                table: "fuel_type_product_codes");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_types_brands_brand_id",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_zero_price_needs_no_pump_price",
                table: "fuel_types");

            migrationBuilder.DropIndex(
                name: "ix_fuel_offer_prices_brand_id",
                table: "fuel_offer_prices");

            migrationBuilder.AlterColumn<decimal>(
                name: "margin_uah_per_liter",
                table: "fuel_types",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldNullable: true);

        }
    }
}
