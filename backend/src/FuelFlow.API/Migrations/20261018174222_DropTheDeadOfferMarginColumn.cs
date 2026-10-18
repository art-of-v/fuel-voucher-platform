using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DropTheDeadOfferMarginColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offers_margin_signs",
                table: "fuel_offers");

            migrationBuilder.DropColumn(
                name: "margin_uah_per_liter",
                table: "fuel_offers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "margin_uah_per_liter",
                table: "fuel_offers",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-95",
                column: "margin_uah_per_liter",
                value: 2.0m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-dp",
                column: "margin_uah_per_liter",
                value: 2.0m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-gas",
                column: "margin_uah_per_liter",
                value: 1.5m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-p95",
                column: "margin_uah_per_liter",
                value: 3.0m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                column: "margin_uah_per_liter",
                value: 2.5m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-100",
                column: "margin_uah_per_liter",
                value: 3.0m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-95",
                column: "margin_uah_per_liter",
                value: 2.0m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                column: "margin_uah_per_liter",
                value: 2.5m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-dp",
                column: "margin_uah_per_liter",
                value: 2.0m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-gas",
                column: "margin_uah_per_liter",
                value: 1.5m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offers_margin_signs",
                table: "fuel_offers",
                sql: "margin_uah_per_liter IS NULL OR margin_uah_per_liter >= 0");
        }
    }
}
