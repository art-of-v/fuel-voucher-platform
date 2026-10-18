using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class StationsBecomeBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contracts_stations_station_id",
                table: "contracts");

            // Both tables arrived by rename: they are the same rows under a name that says what they
            // are. stations was a brand network and station_nodes a filling station inside one -
            // which is why six tables had a column called station_id holding something that is not
            // a station id. Dropping either table would orphan every row that points at it, so the
            // rename is the whole operation and nothing below recreates a row.
            migrationBuilder.RenameTable(
                name: "stations",
                newName: "brands");

            migrationBuilder.RenameTable(
                name: "station_nodes",
                newName: "brand_stations");

            // EF had planned to drop and recreate this table, so it never emitted the column rename
            // that the rename above needs. Its index kept the old name for the same reason.
            migrationBuilder.RenameColumn(
                name: "station_id",
                table: "brand_stations",
                newName: "brand_id");

            migrationBuilder.RenameIndex(
                name: "IX_station_nodes_station_id",
                table: "brand_stations",
                newName: "IX_brand_stations_brand_id");

            // PostgreSQL carries constraints across a table rename, names included, so these four still say
            // `stations` / `station_nodes` and would leave the error text pointing at tables that no
            // longer exist. Declared as drop-and-add rather than raw SQL so the check that compares
            // the model against what the migrations install can still see them.
            migrationBuilder.DropCheckConstraint(name: "ck_stations_id_length", table: "brands");
            migrationBuilder.AddCheckConstraint(name: "ck_brands_id_length", table: "brands", sql: "length(id) <= 100");

            migrationBuilder.DropCheckConstraint(name: "ck_stations_name_length", table: "brands");
            migrationBuilder.AddCheckConstraint(name: "ck_brands_name_length", table: "brands", sql: "length(name) <= 200");

            migrationBuilder.DropCheckConstraint(name: "ck_station_nodes_id_length", table: "brand_stations");
            migrationBuilder.AddCheckConstraint(name: "ck_brand_stations_id_length", table: "brand_stations", sql: "length(id) <= 100");

            migrationBuilder.DropCheckConstraint(name: "ck_station_nodes_station_id_length", table: "brand_stations");
            migrationBuilder.AddCheckConstraint(name: "ck_brand_stations_brand_id_length", table: "brand_stations", sql: "length(brand_id) <= 100");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_station_id_length",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_type_product_codes_station_id_length",
                table: "fuel_type_product_codes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offers_station_id_length",
                table: "fuel_offers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offer_prices_station_id_length",
                table: "fuel_offer_prices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_station_id_length",
                table: "contracts");

            migrationBuilder.RenameColumn(
                name: "station_id",
                table: "fuel_types",
                newName: "brand_id");

            migrationBuilder.RenameIndex(
                name: "ux_fuel_types_station_name",
                table: "fuel_types",
                newName: "ux_fuel_types_brand_name");

            migrationBuilder.RenameIndex(
                name: "ux_fuel_types_station_brand_fuel_code",
                table: "fuel_types",
                newName: "ux_fuel_types_brand_brand_fuel_code");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_types_station_id",
                table: "fuel_types",
                newName: "IX_fuel_types_brand_id");

            migrationBuilder.RenameColumn(
                name: "station_id",
                table: "fuel_type_product_codes",
                newName: "brand_id");

            migrationBuilder.RenameIndex(
                name: "ux_fuel_type_product_codes_station_code",
                table: "fuel_type_product_codes",
                newName: "ux_fuel_type_product_codes_brand_code");

            migrationBuilder.RenameColumn(
                name: "station_id",
                table: "fuel_offers",
                newName: "brand_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_offers_station_id_fuel_type_id",
                table: "fuel_offers",
                newName: "IX_fuel_offers_brand_id_fuel_type_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_offers_station_id",
                table: "fuel_offers",
                newName: "IX_fuel_offers_brand_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_offers_station_fuel_pool",
                table: "fuel_offers",
                newName: "ix_fuel_offers_brand_fuel_pool");

            migrationBuilder.RenameColumn(
                name: "station_id",
                table: "fuel_offer_prices",
                newName: "brand_id");

            migrationBuilder.RenameColumn(
                name: "station_id",
                table: "contracts",
                newName: "brand_id");

            migrationBuilder.RenameIndex(
                name: "IX_contracts_station_id",
                table: "contracts",
                newName: "IX_contracts_brand_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_brand_id_length",
                table: "fuel_types",
                sql: "length(brand_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_type_product_codes_brand_id_length",
                table: "fuel_type_product_codes",
                sql: "length(brand_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offers_brand_id_length",
                table: "fuel_offers",
                sql: "length(brand_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offer_prices_brand_id_length",
                table: "fuel_offer_prices",
                sql: "length(brand_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_brand_id_length",
                table: "contracts",
                sql: "length(brand_id) <= 100");

            migrationBuilder.AddForeignKey(
                name: "FK_contracts_brands_brand_id",
                table: "contracts",
                column: "brand_id",
                principalTable: "brands",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_brand_stations_brands_brand_id",
                table: "brand_stations",
                column: "brand_id",
                principalTable: "brands",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contracts_brands_brand_id",
                table: "contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_brand_stations_brands_brand_id",
                table: "brand_stations");

            migrationBuilder.DropTable(
                name: "brand_stations");

            migrationBuilder.DropTable(
                name: "brands");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_brand_id_length",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_type_product_codes_brand_id_length",
                table: "fuel_type_product_codes");

            migrationBuilder.DropIndex(
                name: "IX_fuel_offers_fuel_type_id",
                table: "fuel_offers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offers_brand_id_length",
                table: "fuel_offers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offer_prices_brand_id_length",
                table: "fuel_offer_prices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_brand_id_length",
                table: "contracts");

            migrationBuilder.RenameColumn(
                name: "brand_id",
                table: "fuel_types",
                newName: "station_id");

            migrationBuilder.RenameIndex(
                name: "ux_fuel_types_brand_name",
                table: "fuel_types",
                newName: "ux_fuel_types_station_name");

            migrationBuilder.RenameIndex(
                name: "ux_fuel_types_brand_brand_fuel_code",
                table: "fuel_types",
                newName: "ux_fuel_types_station_brand_fuel_code");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_types_brand_id",
                table: "fuel_types",
                newName: "IX_fuel_types_station_id");

            migrationBuilder.RenameColumn(
                name: "brand_id",
                table: "fuel_type_product_codes",
                newName: "station_id");

            migrationBuilder.RenameIndex(
                name: "ux_fuel_type_product_codes_brand_code",
                table: "fuel_type_product_codes",
                newName: "ux_fuel_type_product_codes_station_code");

            migrationBuilder.RenameColumn(
                name: "brand_id",
                table: "fuel_offers",
                newName: "station_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_offers_brand_id_fuel_type_id",
                table: "fuel_offers",
                newName: "IX_fuel_offers_station_id_fuel_type_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_offers_brand_id",
                table: "fuel_offers",
                newName: "IX_fuel_offers_station_id");

            migrationBuilder.RenameIndex(
                name: "ix_fuel_offers_brand_fuel_pool",
                table: "fuel_offers",
                newName: "ix_fuel_offers_station_fuel_pool");

            migrationBuilder.RenameColumn(
                name: "brand_id",
                table: "fuel_offer_prices",
                newName: "station_id");

            migrationBuilder.RenameColumn(
                name: "brand_id",
                table: "contracts",
                newName: "station_id");

            migrationBuilder.RenameIndex(
                name: "IX_contracts_brand_id",
                table: "contracts",
                newName: "IX_contracts_station_id");

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "#00ff80"),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: true),
                    lng = table.Column<double>(type: "double precision", nullable: true),
                    logo_text = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 999),
                    station_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.id);
                    table.CheckConstraint("ck_stations_id_length", "length(id) <= 100");
                    table.CheckConstraint("ck_stations_name_length", "length(name) <= 200");
                });

            migrationBuilder.CreateTable(
                name: "station_nodes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    city = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: true),
                    lng = table.Column<double>(type: "double precision", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    station_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station_nodes", x => x.id);
                    table.CheckConstraint("ck_station_nodes_id_length", "length(id) <= 100");
                    table.CheckConstraint("ck_station_nodes_station_id_length", "length(station_id) <= 100");
                    table.ForeignKey(
                        name: "FK_station_nodes_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "stations",
                columns: new[] { "id", "address", "color", "created_at_utc", "deleted_at_utc", "is_deleted", "lat", "lng", "logo_text", "name", "phone", "sort_order", "station_type", "updated_at_utc" },
                values: new object[,]
                {
                    { "klo", null, "#eab308", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50.4101, 30.403400000000001, "KLO", "KLO", null, 4, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko", null, "#22c55e", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50.485100000000003, 30.473400000000002, "OKKO", "OKKO", null, 1, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "upg", null, "#06b6d4", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50.400100000000002, 30.613399999999999, "UPG", "UPG", null, 3, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog", null, "#10b981", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50.450099999999999, 30.523399999999999, "WOG", "WOG", null, 2, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_station_id_length",
                table: "fuel_types",
                sql: "length(station_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_type_product_codes_station_id_length",
                table: "fuel_type_product_codes",
                sql: "length(station_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offers_station_id_length",
                table: "fuel_offers",
                sql: "length(station_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offer_prices_station_id_length",
                table: "fuel_offer_prices",
                sql: "length(station_id) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_station_id_length",
                table: "contracts",
                sql: "length(station_id) <= 100");

            migrationBuilder.CreateIndex(
                name: "IX_station_nodes_station_id",
                table: "station_nodes",
                column: "station_id");

            migrationBuilder.AddForeignKey(
                name: "FK_brand_stations_stations_station_id",
                table: "brand_stations",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
