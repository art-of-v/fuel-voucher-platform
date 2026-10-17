using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Adds <c>fuel_type_product_codes</c>: the QR product codes a network's vouchers carry, and the
    /// grade each one means there. Planning #349, step 3b.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table and not a column on <c>fuel_types</c>, because the cardinality does not fit a column:
    /// diesel at OKKO is 9018 <em>and</em> 9518, A-95 is 9015, 9016, 9515 and 9009. One grade, several
    /// codes — which is also why the uniqueness is per <c>(station_id, product_code)</c>: a code
    /// identifies a grade within the network that publishes it, and we cannot prove two networks do not
    /// share a numbering space.
    /// </para>
    /// <para>
    /// <b>Empty, and nothing backfills it.</b> The codes are observed rather than derived: they are what
    /// paper in the wild carries, and the only source of that truth is the paper. A migration that
    /// inferred them from the category enum would be writing an identity from a guess — and this one
    /// decides what a paid-for voucher is worth. The parser reads the table when it is filled and falls
    /// back to the category path while it is not, so this is additive from the first deploy.
    /// </para>
    /// </remarks>
    public partial class AddFuelTypeProductCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_type_product_codes",
                columns: table => new
                {
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    product_code = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_type_product_codes", x => new { x.fuel_type_id, x.product_code });
                    table.CheckConstraint("ck_fuel_type_product_codes_code_not_blank", "btrim(product_code) <> ''");
                    table.CheckConstraint("ck_fuel_type_product_codes_fuel_type_id_length", "length(fuel_type_id) <= 100");
                    table.CheckConstraint("ck_fuel_type_product_codes_product_code_length", "length(product_code) <= 32");
                    table.CheckConstraint("ck_fuel_type_product_codes_station_id_length", "length(station_id) <= 100");
                });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_type_product_codes_code",
                table: "fuel_type_product_codes",
                column: "product_code");

            migrationBuilder.CreateIndex(
                name: "ux_fuel_type_product_codes_station_code",
                table: "fuel_type_product_codes",
                columns: new[] { "station_id", "product_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_type_product_codes");
        }
    }
}
