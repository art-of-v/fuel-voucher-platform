using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class FuelNominalsAreTheSizesASaleExistsIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_nominals",
                columns: table => new
                {
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_nominals", x => new { x.fuel_type_id, x.liters });
                    table.CheckConstraint("ck_fuel_nominals_liters_positive", "liters > 0");
                    table.ForeignKey(
                        name: "FK_fuel_nominals_fuel_types_fuel_type_id",
                        column: x => x.fuel_type_id,
                        principalTable: "fuel_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Backfill from what the catalogue already sells. A fuel_offer_volume is the offer's copy of a
            // nominal, so the distinct pairs across the pools are exactly the sizes each fuel is sold in
            // today. Without this the shelf would empty itself the moment the code stopped reading
            // volumes: "listed" would mean "no row", and no fuel would have any.
            migrationBuilder.Sql(
                """
                INSERT INTO fuel_nominals (fuel_type_id, liters, created_at_utc, updated_at_utc)
                SELECT DISTINCT o.fuel_type_id, v.liters, now(), now()
                  FROM fuel_offer_volumes v
                  JOIN fuel_offers o ON o.id = v.offer_id
                 WHERE NOT v.is_deleted
                   AND NOT o.is_deleted
                   AND v.liters > 0
                ON CONFLICT DO NOTHING;
                """);
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_nominals");
        }
    }
}
