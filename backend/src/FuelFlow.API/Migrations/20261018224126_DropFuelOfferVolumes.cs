using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DropFuelOfferVolumes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_offer_volumes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_offer_volumes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    offer_id = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_offer_volumes", x => x.id);
                    table.CheckConstraint("ck_fuel_offer_volumes_id_length", "length(id) <= 100");
                    table.CheckConstraint("ck_fuel_offer_volumes_liters_positive", "liters > 0");
                    table.CheckConstraint("ck_fuel_offer_volumes_offer_id_length", "length(offer_id) <= 103");
                    table.ForeignKey(
                        name: "FK_fuel_offer_volumes_fuel_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "fuel_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "fuel_offer_volumes",
                columns: new[] { "id", "created_at_utc", "deleted_at_utc", "is_deleted", "liters", "offer_id", "updated_at_utc" },
                values: new object[,]
                {
                    { "okko-95-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "okko-95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-95-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "okko-95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-95-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "okko-95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-dp-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "okko-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-dp-2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 2m, "okko-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-dp-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "okko-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-dp-3", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 3m, "okko-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-dp-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "okko-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-gas-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "okko-gas", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-gas-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "okko-gas", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-gas-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "okko-gas", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-p95-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "okko-p95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-p95-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "okko-p95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-p95-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "okko-p95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-pulls-dp-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "okko-pulls-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-pulls-dp-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "okko-pulls-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "okko-pulls-dp-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "okko-pulls-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-100-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "wog-100", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-100-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "wog-100", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-100-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "wog-100", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-95-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "wog-95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-95-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "wog-95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-95-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "wog-95", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-95-euro-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "wog-95-euro", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-95-euro-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "wog-95-euro", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-95-euro-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "wog-95-euro", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-dp-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "wog-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-dp-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "wog-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-dp-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "wog-dp", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-gas-10", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 10m, "wog-gas", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-gas-20", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 20m, "wog-gas", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "wog-gas-50", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 50m, "wog-gas", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_offer_volumes_is_deleted_liters",
                table: "fuel_offer_volumes",
                columns: new[] { "is_deleted", "liters" });

            migrationBuilder.CreateIndex(
                name: "ux_fuel_offer_volumes_offer_liters",
                table: "fuel_offer_volumes",
                columns: new[] { "offer_id", "liters" },
                unique: true);
        }
    }
}
