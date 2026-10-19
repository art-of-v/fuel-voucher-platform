using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class OfferPricingBecomesTheFuel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_offer_prices_fuel_offers_offer_id",
                table: "fuel_offer_prices");

            migrationBuilder.DropForeignKey(
                name: "FK_journal_lines_fuel_offers_offer_id",
                table: "journal_lines");

            migrationBuilder.DropTable(
                name: "fuel_offers");

            migrationBuilder.DropIndex(
                name: "IX_journal_lines_offer_id",
                table: "journal_lines");

            migrationBuilder.DropIndex(
                name: "ix_fuel_offer_prices_offer_effective",
                table: "fuel_offer_prices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offer_prices_offer_id_length",
                table: "fuel_offer_prices");

            migrationBuilder.DropColumn(
                name: "offer_id",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "offer_id",
                table: "fuel_offer_prices");

            migrationBuilder.AddColumn<decimal>(
                name: "margin_percent",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "price_updated_at",
                table: "fuel_types",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "price_updated_by_user_id",
                table: "fuel_types",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pump_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "supplier_price_per_liter",
                table: "fuel_types",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-dp",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-100",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-dp",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-gas",
                columns: new[] { "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            // ---------------------------------------------------------------------------------
            // MOVE THE PRICES. This is the part a scaffolded migration never writes, and without it
            // every fuel in production comes out of this migration unpriced: the storefront skips
            // it, checkout refuses it, and the shelf is empty until somebody types the numbers back
            // in by hand.
            //
            // A fuel could own two offers - one per pool - and they could disagree, because that is
            // what the old schema allowed. One price per grade is what this change settles on, so
            // the live pool's row wins and the rehearsal one is dropped: a rehearsal catalogue that
            // quotes a different litre from the live one is the behaviour being removed, and
            // carrying it forward into a single column would make the choice arbitrary. Ordered by
            // updated_at so the tie is at least the most recently touched row rather than whichever
            // Postgres returned first.
            migrationBuilder.Sql("""
                UPDATE fuel_types AS f
                SET supplier_price_per_liter = src.supplier_price_per_liter,
                    pump_price_per_liter     = src.pump_price_per_liter,
                    min_discount_per_liter   = src.min_discount_per_liter,
                    margin_percent           = src.margin_percent,
                    price_updated_at         = src.price_updated_at,
                    price_updated_by_user_id = src.price_updated_by_user_id
                FROM (
                    SELECT DISTINCT ON (fuel_type_id)
                           fuel_type_id,
                           supplier_price_per_liter,
                           pump_price_per_liter,
                           min_discount_per_liter,
                           margin_percent,
                           price_updated_at,
                           price_updated_by_user_id
                    FROM fuel_offers
                    ORDER BY fuel_type_id, (data_kind = 'real') DESC, updated_at_utc DESC
                ) AS src
                WHERE f.id = src.fuel_type_id;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_margin_signs",
                table: "fuel_types",
                sql: "margin_uah_per_liter IS NULL OR margin_uah_per_liter >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_pricing_knobs_non_negative",
                table: "fuel_types",
                sql: "(supplier_price_per_liter IS NULL OR supplier_price_per_liter >= 0) AND (pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND (min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_margin_signs",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_pricing_knobs_non_negative",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "margin_percent",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "min_discount_per_liter",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "price_updated_at",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "price_updated_by_user_id",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "pump_price_per_liter",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "supplier_price_per_liter",
                table: "fuel_types");

            migrationBuilder.AddColumn<string>(
                name: "offer_id",
                table: "journal_lines",
                type: "text",
                maxLength: 103,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "offer_id",
                table: "fuel_offer_prices",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "fuel_offers",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    data_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    margin_percent = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    min_discount_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    price_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    price_updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pump_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    supplier_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_offers", x => x.id);
                    table.CheckConstraint("ck_fuel_offers_data_kind", "data_kind IN ('real', 'qa')");
                    table.CheckConstraint("ck_fuel_offers_fuel_type_id_length", "length(fuel_type_id) <= 100");
                    table.CheckConstraint("ck_fuel_offers_id_length", "length(id) <= 103");
                    table.CheckConstraint("ck_fuel_offers_prices_non_negative", "(supplier_price_per_liter IS NULL OR supplier_price_per_liter >= 0) AND (pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND (min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
                    table.ForeignKey(
                        name: "FK_fuel_offers_fuel_types_fuel_type_id",
                        column: x => x.fuel_type_id,
                        principalTable: "fuel_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "fuel_offers",
                columns: new[] { "id", "brand_id", "created_at_utc", "data_kind", "deleted_at_utc", "fuel_type_id", "is_deleted", "margin_percent", "min_discount_per_liter", "price_updated_at", "price_updated_by_user_id", "pump_price_per_liter", "supplier_price_per_liter", "updated_at_utc" },
                values: new object[,]
                {
                    { "okko-95", new Guid("11111111-1111-4111-8111-111111111111"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "okko-95", false, null, null, null, null, null, 49.0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-dp", new Guid("11111111-1111-4111-8111-111111111111"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "okko-dp", false, null, null, null, null, null, 50.0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-gas", new Guid("11111111-1111-4111-8111-111111111111"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "okko-gas", false, null, null, null, null, null, 25.5m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-p95", new Guid("11111111-1111-4111-8111-111111111111"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "okko-p95", false, null, null, null, null, null, 53.0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-pulls-dp", new Guid("11111111-1111-4111-8111-111111111111"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "okko-pulls-dp", false, null, null, null, null, null, 52.5m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-100", new Guid("22222222-2222-4222-8222-222222222222"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "wog-100", false, null, null, null, null, null, 58.0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-95", new Guid("22222222-2222-4222-8222-222222222222"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "wog-95", false, null, null, null, null, null, 50.0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-95-euro", new Guid("22222222-2222-4222-8222-222222222222"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "wog-95-euro", false, null, null, null, null, null, 50.5m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-dp", new Guid("22222222-2222-4222-8222-222222222222"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "wog-dp", false, null, null, null, null, null, 51.0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-gas", new Guid("22222222-2222-4222-8222-222222222222"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "real", null, "wog-gas", false, null, null, null, null, null, 25.5m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            // Move the prices back onto the offers just re-inserted, which carry the 2026-01-01 seed
            // figures. Without this a Down would leave every fuel trading at its seed price instead of
            // the one it was actually selling at - a rollback that silently reprices the catalogue is
            // worse than no rollback at all.
            migrationBuilder.Sql("""
                UPDATE fuel_offers AS o
                SET supplier_price_per_liter = f.supplier_price_per_liter,
                    pump_price_per_liter     = f.pump_price_per_liter,
                    min_discount_per_liter   = f.min_discount_per_liter,
                    margin_percent           = f.margin_percent,
                    price_updated_at         = f.price_updated_at,
                    price_updated_by_user_id = f.price_updated_by_user_id
                FROM fuel_types AS f
                WHERE f.id = o.fuel_type_id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_journal_lines_offer_id",
                table: "journal_lines",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_offer_prices_offer_effective",
                table: "fuel_offer_prices",
                columns: new[] { "offer_id", "effective_from_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offer_prices_offer_id_length",
                table: "fuel_offer_prices",
                sql: "length(offer_id) <= 103");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_offers_brand_fuel_pool",
                table: "fuel_offers",
                columns: new[] { "brand_id", "fuel_type_id", "data_kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offers_brand_id",
                table: "fuel_offers",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offers_brand_id_fuel_type_id",
                table: "fuel_offers",
                columns: new[] { "brand_id", "fuel_type_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offers_fuel_type_id",
                table: "fuel_offers",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offers_is_deleted_data_kind",
                table: "fuel_offers",
                columns: new[] { "is_deleted", "data_kind" });

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_offer_prices_fuel_offers_offer_id",
                table: "fuel_offer_prices",
                column: "offer_id",
                principalTable: "fuel_offers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_journal_lines_fuel_offers_offer_id",
                table: "journal_lines",
                column: "offer_id",
                principalTable: "fuel_offers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
