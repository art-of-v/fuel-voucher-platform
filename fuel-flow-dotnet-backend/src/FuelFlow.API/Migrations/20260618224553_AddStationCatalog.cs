using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddStationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_packages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    fuel_name = table.Column<string>(type: "text", nullable: false),
                    liters = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    original_price = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_packages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fuel_types",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    base_price = table.Column<int>(type: "integer", nullable: false),
                    discount_price = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false, defaultValue: "#00ff80"),
                    logo_text = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    station_type = table.Column<string>(type: "text", nullable: true),
                    lat = table.Column<string>(type: "text", nullable: true),
                    lng = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "station_nodes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    station_type = table.Column<string>(type: "text", nullable: true),
                    lat = table.Column<string>(type: "text", nullable: true),
                    lng = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station_nodes", x => x.id);
                    table.ForeignKey(
                        name: "FK_station_nodes_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "fuel_packages",
                columns: new[] { "id", "created_at_utc", "fuel_name", "fuel_type_id", "liters", "original_price", "price", "station_id" },
                values: new object[,]
                {
                    { "okko-95-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A-95", "okko-95", 10, 550, 520, "okko" },
                    { "okko-95-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A-95", "okko-95", 20, 1100, 1040, "okko" },
                    { "okko-95-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A-95", "okko-95", 50, 2750, 2600, "okko" },
                    { "okko-dp-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП ЄВРО", "okko-dp", 10, 550, 520, "okko" },
                    { "okko-dp-2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП ЄВРО", "okko-dp", 2, 110, 104, "okko" },
                    { "okko-dp-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП ЄВРО", "okko-dp", 20, 1100, 1040, "okko" },
                    { "okko-dp-3", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП ЄВРО", "okko-dp", 3, 165, 156, "okko" },
                    { "okko-dp-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП ЄВРО", "okko-dp", 50, 2750, 2600, "okko" },
                    { "okko-gas-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ГАЗ", "okko-gas", 10, 300, 280, "okko" },
                    { "okko-gas-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ГАЗ", "okko-gas", 20, 600, 560, "okko" },
                    { "okko-gas-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ГАЗ", "okko-gas", 50, 1500, 1400, "okko" },
                    { "okko-p95-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Pulls 95", "okko-p95", 10, 620, 580, "okko" },
                    { "okko-p95-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Pulls 95", "okko-p95", 20, 1240, 1160, "okko" },
                    { "okko-p95-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Pulls 95", "okko-p95", 50, 3100, 2900, "okko" },
                    { "okko-pulls-dp-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП PULLS", "okko-pulls-dp", 10, 580, 550, "okko" },
                    { "okko-pulls-dp-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП PULLS", "okko-pulls-dp", 20, 1160, 1100, "okko" },
                    { "okko-pulls-dp-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП PULLS", "okko-pulls-dp", 50, 2900, 2750, "okko" },
                    { "wog-100-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Mustang 100", "wog-100", 10, 650, 600, "wog" },
                    { "wog-100-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Mustang 100", "wog-100", 20, 1300, 1200, "wog" },
                    { "wog-100-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Mustang 100", "wog-100", 50, 3250, 3000, "wog" },
                    { "wog-95-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A-95 Mustang", "wog-95", 10, 560, 530, "wog" },
                    { "wog-95-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A-95 Mustang", "wog-95", 20, 1120, 1060, "wog" },
                    { "wog-95-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A-95 Mustang", "wog-95", 50, 2800, 2650, "wog" },
                    { "wog-95-euro-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A 95 EURO", "wog-95-euro", 10, 550, 520, "wog" },
                    { "wog-95-euro-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A 95 EURO", "wog-95-euro", 20, 1100, 1040, "wog" },
                    { "wog-95-euro-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A 95 EURO", "wog-95-euro", 50, 2750, 2600, "wog" },
                    { "wog-dp-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП Mustang", "wog-dp", 10, 560, 530, "wog" },
                    { "wog-dp-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП Mustang", "wog-dp", 20, 1120, 1060, "wog" },
                    { "wog-dp-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ДП Mustang", "wog-dp", 50, 2800, 2650, "wog" },
                    { "wog-gas-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ГАЗ", "wog-gas", 10, 300, 280, "wog" },
                    { "wog-gas-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ГАЗ", "wog-gas", 20, 600, 560, "wog" },
                    { "wog-gas-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ГАЗ", "wog-gas", 50, 1500, 1400, "wog" }
                });

            migrationBuilder.InsertData(
                table: "fuel_types",
                columns: new[] { "id", "base_price", "created_at_utc", "discount_price", "name", "station_id" },
                values: new object[,]
                {
                    { "okko-95", 55, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 52, "A-95", "okko" },
                    { "okko-dp", 55, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 52, "ДП ЄВРО", "okko" },
                    { "okko-gas", 30, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 28, "ГАЗ", "okko" },
                    { "okko-p95", 62, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 58, "Pulls 95", "okko" },
                    { "okko-pulls-dp", 58, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 55, "ДП PULLS", "okko" },
                    { "wog-100", 65, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 60, "Mustang 100", "wog" },
                    { "wog-95", 56, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 53, "A-95 Mustang", "wog" },
                    { "wog-95-euro", 55, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 52, "A 95 EURO", "wog" },
                    { "wog-dp", 56, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 53, "ДП Mustang", "wog" },
                    { "wog-gas", 30, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 28, "ГАЗ", "wog" }
                });

            migrationBuilder.InsertData(
                table: "stations",
                columns: new[] { "id", "address", "color", "created_at_utc", "lat", "lng", "logo_text", "name", "phone", "station_type" },
                values: new object[,]
                {
                    { "klo", null, "#eab308", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "50.4101", "30.4034", "KLO", "KLO", null, null },
                    { "okko", null, "#22c55e", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "50.4851", "30.4734", "OKKO", "OKKO", null, null },
                    { "upg", null, "#06b6d4", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "50.4001", "30.6134", "UPG", "UPG", null, null },
                    { "wog", null, "#10b981", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "50.4501", "30.5234", "WOG", "WOG", null, null }
                });

            migrationBuilder.InsertData(
                table: "station_nodes",
                columns: new[] { "id", "address", "city", "created_at_utc", "lat", "lng", "name", "phone", "station_id", "station_type" },
                values: new object[,]
                {
                    { "klo-kyiv-main", "Броварський проспект, 11", "Київ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "50.4578", "30.5986", "KLO Київ", null, "klo", "Тип АЗС KLO-міська" },
                    { "okko-kyiv-main", "42 Чоколівський бульвар", "Київ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "50.4310", "30.4515", "OKKO Київ", null, "okko", "Тип АЗС ОККО-міська" },
                    { "upg-kyiv-main", "Проспект Перемоги, 98", "Київ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "50.4566", "30.3950", "UPG Київ", null, "upg", "Тип АЗС UPG-міська" },
                    { "wog-kyiv-main", "15-Б проспект Соборності", "Київ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "50.4482", "30.6170", "WOG Київ", null, "wog", "Тип АЗС WOG-міська" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_fuel_type_id",
                table: "fuel_packages",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_station_id",
                table: "fuel_packages",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_types_station_id",
                table: "fuel_types",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_station_nodes_station_id",
                table: "station_nodes",
                column: "station_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_packages");

            migrationBuilder.DropTable(
                name: "fuel_types");

            migrationBuilder.DropTable(
                name: "station_nodes");

            migrationBuilder.DropTable(
                name: "stations");
        }
    }
}
